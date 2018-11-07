using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Web;
using TechnitiumLibrary.IO;

namespace DNS_over_HTTPS
{
    public class Global : HttpApplication
    {
        static IPEndPoint _dnsServerEndPoint;

        protected void Application_Start(object sender, EventArgs e)
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
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
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
        }
    }
}