/*
Technitium DNS-over-HTTPS
Copyright (C) 2022  Shreyas Zare (shreyas@technitium.com)

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
                _dnsServers[i] = new NameServerAddress(dnsServersList[i]);

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
                HttpRequest Request = context.Request;
                HttpResponse Response = context.Response;

                if (Request.Path == "/dns-query")
                {
                    try
                    {
                        string acceptTypes = Request.Headers["accept"];
                        if ((acceptTypes != null) && !acceptTypes.Contains("application/dns-message"))
                            throw new NotSupportedException("DoH request type not supported.");

                        DnsDatagram request;

                        switch (Request.Method)
                        {
                            case "GET":
                                string strRequest = Request.Query["dns"];
                                if (string.IsNullOrEmpty(strRequest))
                                    throw new ArgumentNullException("dns");

                                //convert from base64url to base64
                                strRequest = strRequest.Replace('-', '+');
                                strRequest = strRequest.Replace('_', '/');

                                //add padding
                                int x = strRequest.Length % 4;
                                if (x > 0)
                                    strRequest = strRequest.PadRight(strRequest.Length - x + 4, '=');

                                request = DnsDatagram.ReadFrom(new MemoryStream(Convert.FromBase64String(strRequest)));
                                break;

                            case "POST":
                                if (Request.ContentType != "application/dns-message")
                                    throw new NotSupportedException("DNS request type not supported: " + Request.ContentType);

                                using (MemoryStream mS = new MemoryStream())
                                {
                                    await Request.Body.CopyToAsync(mS);

                                    mS.Position = 0;
                                    request = DnsDatagram.ReadFrom(mS);
                                }

                                break;

                            default:
                                throw new NotSupportedException("DoH request type not supported.");
                        }

                        DnsClient dnsClient = new DnsClient(_dnsServers);

                        dnsClient.Timeout = _timeout;
                        dnsClient.Retries = _retries;
                        dnsClient.Concurrency = _dnsServers.Length;

                        DnsDatagram response = await dnsClient.ResolveAsync(request);

                        Response.ContentType = "application/dns-message";

                        using (MemoryStream mS = new MemoryStream())
                        {
                            response.WriteTo(mS);

                            mS.Position = 0;
                            await mS.CopyToAsync(Response.Body);
                        }
                    }
                    catch (Exception ex)
                    {
                        Response.Clear();
                        Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        await Response.WriteAsync("<h1>500 Internal Server Error</h1>");

                        if (_debug)
                            await Response.WriteAsync("<p>" + ex.ToString() + "</p>");
                    }
                }
                else
                {
                    Response.StatusCode = (int)HttpStatusCode.NotFound;
                    await Response.WriteAsync("<h1>404 Not Found</h1>");
                }
            });
        }
    }
}
