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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Web;
using TechnitiumLibrary.IO;

namespace DNS_over_HTTPS
{
    public class DnsQueryModule : IHttpModule
    {
        static IPEndPoint _dnsServerEndPoint;

        public void Init(HttpApplication context)
        {
            string[] parts = Properties.Settings.Default.DnsServer.Split(':');
            string host = parts[0];
            int port;

            if (parts.Length > 1)
                port = int.Parse(parts[1]);
            else
                port = 53;

            IPAddress address;

            if (!IPAddress.TryParse(host, out address))
                address = Dns.GetHostAddresses(host)[0];

            _dnsServerEndPoint = new IPEndPoint(address, port);

            context.BeginRequest += delegate (object sender, EventArgs e)
            {
                HttpRequest Request = context.Request;
                HttpResponse Response = context.Response;

                if (Request.Path == "/dns-query")
                {
                    if (!Request.AcceptTypes.Contains("application/dns-message"))
                        throw new NotSupportedException("DoH request type not supported.");

                    byte[] dnsRequest;

                    switch (Request.HttpMethod)
                    {
                        case "GET":
                            dnsRequest = Convert.FromBase64String(Request.QueryString["dns"]);
                            break;

                        case "POST":
                            if (Request.ContentType != "application/dns-message")
                                throw new NotSupportedException("DNS request type not supported: " + Request.ContentType);

                            dnsRequest = Request.InputStream.ReadBytes(Convert.ToInt32(Request.InputStream.Length));
                            break;

                        default:
                            throw new NotSupportedException("DoH request type not supported."); ;
                    }

                    switch (Properties.Settings.Default.DnsServerProtocol.ToLower())
                    {
                        case "tcp":
                            using (Socket socket = new Socket(_dnsServerEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
                            {
                                try
                                {
                                    socket.SendTimeout = Properties.Settings.Default.DnsTimeout;
                                    socket.ReceiveTimeout = Properties.Settings.Default.DnsTimeout;

                                    IAsyncResult result = socket.BeginConnect(_dnsServerEndPoint, null, null);
                                    if (!result.AsyncWaitHandle.WaitOne(Properties.Settings.Default.DnsTimeout))
                                        throw new SocketException((int)SocketError.TimedOut);

                                    if (!socket.Connected)
                                        throw new SocketException((int)SocketError.ConnectionRefused);

                                    NetworkStream stream = new NetworkStream(socket);

                                    //send request
                                    {
                                        byte[] lengthBuffer = BitConverter.GetBytes(Convert.ToUInt16(dnsRequest.Length));
                                        Array.Reverse(lengthBuffer);

                                        stream.Write(lengthBuffer);
                                        stream.Write(dnsRequest);
                                    }

                                    //read response
                                    {
                                        byte[] lengthBuffer = stream.ReadBytes(2);
                                        Array.Reverse(lengthBuffer, 0, 2);
                                        int length = BitConverter.ToUInt16(lengthBuffer, 0);

                                        Response.ContentType = "application/dns-message";
                                        stream.CopyTo(Response.OutputStream, 128, length);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Response.Clear();
                                    Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                                    Response.Write("<p>" + ex.ToString() + "</p>");
                                }
                            }
                            break;

                        default:
                            throw new NotSupportedException("DNS forwarder protocol not supported: " + Properties.Settings.Default.DnsServerProtocol);
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