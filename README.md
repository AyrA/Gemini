# Gemini

This project is a full Gemini client and server implementation.

## Client

### Features

- Full implementation of the protocol
- Redirect loop protection
- Support for query prompts and password request when required by the remote
- Fairly robust against servers not fully protocol compliant
- Rendering of gemini files
- Displays images, audio, and video content
- Backward and forward navigation using your browsers history function
- Supports linking to other protocols (provided you have handlers for them)
- Whitelisting of custom server certificates
- Client certificate authentication

### How to use

There are currently no pre-built binaries yet.
You can manually run it without a code editor:

1. [Install the .NET 6](https://dotnet.microsoft.com/en-us/download) if you don't have it yet (use the "SDK" version)
2. Clone this repository or download it as zip
3. Launch the `run.bat` file in the main directory

This will build the application and run it on a random local port,
then it opens your default browser

### Query

A gemini server may ask you for a query or a secret using a single line of descriptive text.
In those cases you will be shown an input field.
Simply input the requested value and press `ENTER` to submit the value.

### Server certificates

Most gemini servers run on self signed certificates.
When you visit such a service, you will be asked to trust the certificate.
It's up to you, whether you want to accept or reject it.
Trusting the certificate will only apply to the gemini client, and not other tools.
If you reject the certificate, the connection will be cancelled.
You can retry the request to get the certificate prompt again.

If the certificate is expired,
you can trust it but the trust is lost once you exit the application.

You're only asked to trust a certificate
if your operating system won't already trust it.
Gemini servers that use certificates signed by a well known CA are trusted automatically.

### Client certificates

Client certificates are how you authenticate yourself (login) to a gemini server.
Most servers don't require this.
If a server requires this, you will be prompted to select one of your identities.
If you don't yet have one, an error message will be shown.

The application will always make anonymous attempts first.

### Planned Features

- Bookmarks
- Start page configuration

## Server

The server is a fully protocol compliant implementation, including requesting client certificates.

It contains a static file host, which is sufficient for most gemini sites,
but it can be extended using custom hosts.

The server is easily extendable by implementing `Gemini.Lib.GeminiHost` and registering it in the server.
The server will automatically search for host implementations in the "Plugins" subdirectory and load them.

### Request Pipeline

Quick explanation of how a request propagates through a host:

*The URL requested by the client has already been fully parsed at this point*

1. `Start()` (Only once before the first request)
2. `IsAccepted(...)` Decides whether the next two functions are called or not
3. `Rewrite(...)` Rewrites url and optionally terminates the pipeline early
4. `Request(...)` Handles the request or passes it on to the next host
5. `Stop()` (Only once after the last request)

The expected behavior of the methods is explained in the example host below.

Note: If no host handles the request, a generic "Not Found" will be returned to the client.

### Expected Host Behavior

Please adhere to these rules when you write your own host:

- All expensive startup/shutdown procedures should be performed in the `Start` and `Stop` method, and not request processing methods or the constructor
- The constructor should never throw exceptions, and is merely used to get access to dependency injection
- `Start` may throw an exception. This will skip the host for the remainder of the application lifetime
- `Stop` and `Dispose` should never throw exceptions
- Hosts should tolerate multiple calls to `Start`/`Stop`/`Dispose`
- `Stop` may be called while requests are still ongoing. The host should either complete the ongoing request, or gracefully fail them
- Hosts should accept calls to `Start` after `Stop` has been called
- `Dispose` may be called without a prior call to `Stop`
- The `AutoDIRegister` attribute should properly be used with either `Singleton` or `Transient`
- `Start`/`Stop`/`Dispose` should be thread safe. The simplest solution is to use `lock(this){/*...*/}` in them.

### Singleton vs. Transient

You may register your host as either a transient or singleton service.
You should not use `Scoped` as this may lead to unexpected disposal of your service.

In general, singleton registration is preferred over transient.
The difference between these two methods is that singleton uses the same instance for all tcp endpoints,
while transient will register a new instance of your host for every listener.

Note: If you register the host as singleton, you may find that `Start()` and `Stop()` are called multiple times,
once for each configured TCP endpoint. This is because each TCP endpoint gets the same instance of your host.
Ensure that your host is resistant to multiple calls to those functions.

### Example Host

Below is a minimal example of a host that sends the IP address of the client back to it.
Explore `GeminiHost` to see all the properties and methods it has.
Many of them provide a default implementation that you can override
if you are not satisfied with the default logic.

```C#
using AyrA.AutoDI;
using Gemini.Lib;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace ChangeMePlease
{
	[AutoDIRegister(AutoDIType.Singleton)]
	public class IpAddressHost : GeminiHost
	{
		private readonly ILogger<IpAddressHost> _logger;
		
		public IpAddressHost(ILogger<IpAddressHost> logger)
		{
			_logger = logger;
		}
		
		public override async Task<GeminiResponse?> Request(Uri url, IPEndPoint client, X509Certificate? cert)
		{
			if(url.PathAndQuery != "/")
			{
				return await Task.FromResult(GeminiResponse.NotFound());
			}
			_logger.LogInformation("Responding to IP request from {addr}", client);
			return await Task.FromResult(GeminiResponse.Ok("# Your IP: " + client.Address.ToString()));
		}
		
		public override bool IsAccepted(Uri _1, IPAddress _2, X509Certificate? _3) => true;
		
		public override void Dispose()
		{
			GC.SuppressFinalize(this);
		}
	}
}
```

#### Method: IsAccepted

This method decides whether a request is accepted or not.
If it returns `false`, the host will be skipped, but request processing continues with other hosts.
If it returns `true`, `Rewrite(...)` and `Request(...)` will be invoked shortly afterwards.

Note: This function has a default implementation that accepts all requests.
If this is what you want, you don't have to override this.

#### Method: Rewrite

This method gets passed the current request url, and should return a url.

The returned url is then used for all subsequent function calls in the request pipeline,
including other hosts. You may return null to abort the request without sending any response.
To implement a more graceful abort, do not return null here,
and return the appropriate response in `Request` instead.

Note: By overriding `Request` to always return null,
the host can effectively be turned into a pure url rewrite middleware.

#### Method: Request

This method is responsible to process the request and return an adequate response.
You may return null to skip the request and continue with the request pipeline.
Returning a non-null value returns said value to the client and ends the request pipeline.

Implementing this method is required, as there is no default implementation in the base class.
If you purely want to work with `Rewrite`, you can hardcode this method to return null.

#### Method: Start

This method is called at least once before the host is added to the request pipeline.
This is the appropriate location to perform long running initialization tasks.

#### Method: Stop

This method is called at least once when it's shut down.
This method is appropriate to clean up objects that consume a lot of memory.

#### Method: Dispose

This is called as the last method in the lifecycle of the host.
No other function will be called after this,
but this may be called multiple times.

This method is an appropriate location to clean up unmanaged resources.