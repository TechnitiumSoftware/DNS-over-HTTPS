/*
Technitium DNS-over-HTTPS
Copyright (C) 2018  Shreyas Zare (shreyas@technitium.com)

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

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using TechnitiumLibrary.Net.Dns;
using TechnitiumLibrary.Net.Dns.ClientConnection;

namespace DNS_over_HTTPS
{
    public class DnsQueryModule : IHttpModule
    {
        static NameServerAddress _dnsServer;

        public void Init(HttpApplication context)
        {
            _dnsServer = new NameServerAddress(Properties.Settings.Default.DnsServer);

            context.BeginRequest += delegate (object sender, EventArgs e)
            {
                HttpRequest Request = context.Request;
                HttpResponse Response = context.Response;

                if (Request.Path == "/dns-query")
                {
                    if (!Request.AcceptTypes.Contains("application/dns-message"))
                        throw new NotSupportedException("DoH request type not supported.");

                    try
                    {
                        DnsDatagram request;

                        switch (Request.HttpMethod)
                        {
                            case "GET":
                                string strRequest = Request.QueryString["dns"];
                                if (string.IsNullOrEmpty(strRequest))
                                    throw new ArgumentNullException("dns");

                                //convert from base64url to base64
                                strRequest = strRequest.Replace('-', '+');
                                strRequest = strRequest.Replace('_', '/');

                                //add padding
                                int x = strRequest.Length % 4;
                                if (x > 0)
                                    strRequest = strRequest.PadRight(strRequest.Length - x + 4, '=');

                                request = new DnsDatagram(new MemoryStream(Convert.FromBase64String(strRequest)));
                                break;

                            case "POST":
                                if (Request.ContentType != "application/dns-message")
                                    throw new NotSupportedException("DNS request type not supported: " + Request.ContentType);

                                request = new DnsDatagram(Request.InputStream);
                                break;

                            default:
                                throw new NotSupportedException("DoH request type not supported."); ;
                        }

                        DnsClientConnection connection = DnsClientConnection.GetConnection((DnsClientProtocol)Enum.Parse(typeof(DnsClientProtocol), Properties.Settings.Default.DnsServerProtocol, true), _dnsServer, null);
                        connection.Timeout = Properties.Settings.Default.DnsTimeout;

                        ushort originalRequestId = request.Header.Identifier;

                        DnsDatagram response = connection.Query(request);
                        if (response == null)
                        {
                            Response.StatusCode = (int)HttpStatusCode.GatewayTimeout;
                            Response.Write("<p>DNS query timed out.</p>");
                        }
                        else
                        {
                            response.Header.SetIdentifier(originalRequestId); //set id since dns connection may change it if 2 clients have same id

                            Response.ContentType = "application/dns-message";

                            using (MemoryStream mS = new MemoryStream())
                            {
                                response.WriteTo(mS);
                                mS.WriteTo(Response.OutputStream);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Response.Clear();
                        Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        Response.Write("<p>" + ex.ToString() + "</p>");
                    }
                }
                else
                {
                    Response.StatusCode = (int)HttpStatusCode.NotFound;
                    Response.Write("<h1>404 Not Found</h1>");
                }

                Response.End();
            };
        }

        public void Dispose()
        {
            //do nothing
        }
    }
}