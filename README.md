# DNS-over-HTTPS
An implementation of RFC 8484 - DNS Queries over HTTPS (DoH). Host your own DoH web service using ASP.NET 5 that can transform any DNS server to be accessible via the DoH standard protocol.

# System Requirements
- Requires [.NET 5](https://dotnet.microsoft.com/download) installed. Install [Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/5.0) for running on Microsoft IIS web server.
- Windows, Linux and macOS supported.

# Download
- **Cross-Platform**: [DoH ASP.NET 5 Web App](https://download.technitium.com/doh/doh-aspnetcore.zip)

# Install Instructions
- **Windows**:
1. Download the `doh-aspnetcore.zip` zip file.
2. Edit the `appsettings.json` file in notepad to set the DNS server of your choice.
3. Install the DoH app on Windows IIS web server by creating a new website and extracting the `doh-aspnetcore.zip` zip file into the wwwroot folder of the website.

Note: You can also run the `DNS-over-HTTPS.exe` to directly run the DoH console app with built in web server.

- **Linux**:
1. Download and extract `doh-aspnetcore.zip` zip file to `/var/aspnetcore/doh`
```
sudo mkdir -p /var/aspnetcore/doh
cd /var/aspnetcore/doh
sudo wget https://download.technitium.com/doh/doh-aspnetcore.zip
sudo unzip doh-aspnetcore.zip
```

2. Edit the `appsettings.json` file in nano to set the DNS server of your choice.
```
sudo nano appsettings.json
```

3. Install the DoH app as a systemd daemon:
```
sudo cp systemd.service /etc/systemd/system/doh.service
sudo systemctl enable doh
sudo systemctl start doh
```

4. Make sure that the DoH daemon is running without issues by running:
```
journalctl --unit doh --follow
```

Note: You can also run `dotnet DNS-over-HTTPS.dll` command to directly run the DoH console app.

The DoH service is available on the `/dns-query` location on the web site that you are running. If you are running it directly as a console app then your DoH end point URL will be `http://localhost:5000/dns-query`. For Linux systemd daemon, the DoH end point will be `http://localhost:8053/dns-query` as per the argument provided in the systemd.service file.

# Blog Posts
[Configuring DNS-over-TLS and DNS-over-HTTPS with any DNS Server](https://blog.technitium.com/2018/12/configuring-dns-over-tls-and-dns-over.html)

# Support
For support, send an email to support@technitium.com. For any issues, feedback, or feature request, create an issue on [GitHub](https://github.com/TechnitiumSoftware/DNS-over-HTTPS/issues).

# Become A Patron
Make contribution to Technitium by becoming a Patron and help making new software, updates, and features possible.

[Become a Patron now!](https://www.patreon.com/technitium)
