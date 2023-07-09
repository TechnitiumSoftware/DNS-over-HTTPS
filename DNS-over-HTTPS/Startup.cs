/*
Technitium DNS-over-HTTPS
Copyright (C) 2023  Shreyas Zare (shreyas@technitium.com)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Net;
using TechnitiumLibrary.Net.Dns;

namespace DNS_over_HTTPS
{
    public class Startup
    {
        static NameServerAddress[] _dnsServers;
        static int _timeout;
        static int _retries;
        static bool _debug;

        public IConfiguration Configuration { get; set; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            DnsTransportProtocol protocol = (DnsTransportProtocol)Enum.Parse(typeof(DnsTransportProtocol), Configuration.GetValue<string>("dnsServerProtocol"), true);
            string dnsServers = Configuration.GetValue<string>("dnsServer");

            string[] dnsServersList = dnsServers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _dnsServers = new NameServerAddress[dnsServersList.Length];

            for (int i = 0; i < _dnsServers.Length; i++)
            {
                _dnsServers[i] = NameServerAddress.Parse(dnsServersList[i]);

                if (_dnsServers[i].Protocol != protocol)
                    _dnsServers[i] = _dnsServers[i].ChangeProtocol(protocol);
            }

            _timeout = Configuration.GetValue<int>("dnsTimeout");
            _retries = Configuration.GetValue<int>("dnsRetries");
            _debug = Configuration.GetValue<bool>("debug");
        }

        public void Configure(IApplicationBuilder app, IHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.Run(async (context) =>
            {
                HttpRequest request = context.Request;
                HttpResponse response = context.Response;

                if (request.Path == "/dns-query")
                {
                    try
                    {
                        DnsDatagram dnsRequest;

                        switch (request.Method)
                        {
                            case "GET":
                                bool acceptsDoH = false;

                                string requestAccept = request.Headers["Accept"];
                                if (string.IsNullOrEmpty(requestAccept))
                                {
                                    acceptsDoH = true;
                                }
                                else
                                {
                                    foreach (string mediaType in requestAccept.Split(','))
                                    {
                                        if (mediaType.Equals("application/dns-message", StringComparison.OrdinalIgnoreCase))
                                        {
                                            acceptsDoH = true;
                                            break;
                                        }
                                    }
                                }

                                if (!acceptsDoH)
                                {
                                    response.StatusCode = 400;
                                    await response.WriteAsync("Bad Request");
                                    return;
                                }

                                string dnsRequestBase64Url = request.Query["dns"];
                                if (string.IsNullOrEmpty(dnsRequestBase64Url))
                                {
                                    response.StatusCode = 400;
                                    await response.WriteAsync("Bad Request");
                                    return;
                                }

                                //convert from base64url to base64
                                dnsRequestBase64Url = dnsRequestBase64Url.Replace('-', '+');
                                dnsRequestBase64Url = dnsRequestBase64Url.Replace('_', '/');

                                //add padding
                                int x = dnsRequestBase64Url.Length % 4;
                                if (x > 0)
                                    dnsRequestBase64Url = dnsRequestBase64Url.PadRight(dnsRequestBase64Url.Length - x + 4, '=');

                                using (MemoryStream mS = new MemoryStream(Convert.FromBase64String(dnsRequestBase64Url)))
                                {
                                    dnsRequest = DnsDatagram.ReadFrom(mS);
                                }

                                break;

                            case "POST":
                                if (!string.Equals(request.Headers["Content-Type"], "application/dns-message", StringComparison.OrdinalIgnoreCase))
                                {
                                    response.StatusCode = 415;
                                    await response.WriteAsync("Unsupported Media Type");
                                    return;
                                }

                                using (MemoryStream mS = new MemoryStream(32))
                                {
                                    await request.Body.CopyToAsync(mS, 32);

                                    mS.Position = 0;
                                    dnsRequest = DnsDatagram.ReadFrom(mS);
                                }

                                break;

                            default:
                                throw new NotSupportedException("DoH request type not supported.");
                        }

                        DnsClient dnsClient = new DnsClient(_dnsServers);

                        dnsClient.Timeout = _timeout;
                        dnsClient.Retries = _retries;
                        dnsClient.Concurrency = _dnsServers.Length;

                        DnsDatagram dnsResponse = await dnsClient.ResolveAsync(dnsRequest);

                        using (MemoryStream mS = new MemoryStream(512))
                        {
                            dnsResponse.WriteTo(mS);

                            mS.Position = 0;
                            response.ContentType = "application/dns-message";
                            response.ContentLength = mS.Length;

                            using (Stream s = response.Body)
                            {
                                await mS.CopyToAsync(s, 512);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        response.Clear();
                        response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        await response.WriteAsync("<h1>500 Internal Server Error</h1>");

                        if (_debug)
                            await response.WriteAsync("<p>" + ex.ToString() + "</p>");
                    }
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    await response.WriteAsync("<h1>404 Not Found</h1>");
                }
            });
        }
    }
}
