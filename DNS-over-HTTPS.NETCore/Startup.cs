using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Net;
using System.Net.Sockets;
using TechnitiumLibrary.IO;

namespace DNS_over_HTTPS.NETCore
{
    public class Startup
    {
        static IPEndPoint _dnsServerEndPoint;

        public IConfiguration Configuration { get; set; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            string[] parts = Configuration.GetValue<string>("DnsServer").Split(':');
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
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
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
                    string acceptTypes = Request.Headers["accept"];

                    if ((acceptTypes != null) && !acceptTypes.Contains("application/dns-message"))
                        throw new NotSupportedException("DoH request type not supported.");

                    byte[] dnsRequest;

                    switch (Request.Method)
                    {
                        case "GET":
                            dnsRequest = Convert.FromBase64String(Request.Query["dns"]);
                            break;

                        case "POST":
                            if (Request.ContentType != "application/dns-message")
                                throw new NotSupportedException("DNS request type not supported: " + Request.ContentType);

                            dnsRequest = Request.Body.ReadBytes(Convert.ToInt32(Request.ContentLength));
                            break;

                        default:
                            throw new NotSupportedException("DoH request type not supported."); ;
                    }

                    string protocol = Configuration.GetValue<string>("DnsServerProtocol");

                    switch (protocol.ToLower())
                    {
                        case "tcp":
                            using (Socket socket = new Socket(_dnsServerEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
                            {
                                try
                                {
                                    int timeout = Configuration.GetValue<int>("DnsTimeout");

                                    socket.SendTimeout = timeout;
                                    socket.ReceiveTimeout = timeout;

                                    IAsyncResult result = socket.BeginConnect(_dnsServerEndPoint, null, null);
                                    if (!result.AsyncWaitHandle.WaitOne(timeout))
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
                                        stream.CopyTo(Response.Body, 128, length);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Response.Clear();
                                    Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                                    await Response.WriteAsync("<h1>500 Internal Server Error</h1>");
                                    await Response.WriteAsync("<p>" + ex.ToString() + "</p>");
                                }
                            }
                            break;

                        default:
                            throw new NotSupportedException("DNS forwarder protocol not supported: " + protocol);
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
