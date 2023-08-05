# Server

The server is a fully protocol compliant implementation, including requesting client certificates.

It contains a static file host, which is sufficient for most gemini sites,
but it can be extended using custom hosts.

The server is easily extendable by implementing `Gemini.Lib.GeminiHost` and registering it in the server.
The server will automatically search for host implementations in the "Plugins" subdirectory and load them.

## Request Pipeline

Quick explanation of how a request propagates through a host:

*The URL requested by the client has already been fully parsed at this point*

1. `Start()` (Only once before the first request)
2. `IsAccepted(...)` Decides whether the next two functions are called or not
3. `Rewrite(...)` Rewrites url and optionally terminates the pipeline early
4. `Request(...)` Handles the request or passes it on to the next host
5. `Stop()` (Only once after the last request)

The expected behavior of the methods is explained in the example host below.

Note: If no host handles the request, a generic "Not Found" will be returned to the client.

## Expected Host Behavior

Please adhere to these rules when you write your own host:

- All expensive startup/shutdown procedures should be performed in the `Start` and `Stop` method, and not request processing methods or the constructor
- The constructor should never throw exceptions, and is merely used to get access to dependency injection
- `Start` may throw an exception. This will skip the host for the remainder of the application lifetime
- `Stop` and `Dispose` should never throw exceptions
- Hosts should tolerate multiple calls to `Start`/`Stop`/`Dispose`
- `Stop` may be called while requests are still ongoing. The host should either complete the ongoing requests, or gracefully fail them
- Hosts should accept calls to `Start` after `Stop` has been called
- `Dispose` may be called without a prior call to `Stop`
- The `AutoDIRegister` attribute should properly be used with either `Singleton` or `Transient`
- `Start`/`Stop`/`Dispose` should be thread safe. The simplest solution is to use `lock(this){/*...*/}` in them.

## Singleton vs. Transient

You may register your host as either a transient or singleton service.
You should not use `Scoped` as this may lead to unexpected disposal of your service.

In general, singleton registration is preferred over transient.
The difference between these two methods is that singleton uses the same instance for all tcp endpoints,
while transient will register a new instance of your host for every listener.

Note: If you register the host as singleton, you may find that `Start()` and `Stop()` are called multiple times,
once for each configured TCP endpoint. This is because each TCP endpoint gets the same instance of your host.
Ensure that your host is resistant to multiple calls to those functions.

## Storing Configuration

Host specific configuration is best stored inside of the folder the host runs from.
An easy way to obtain this path is to use the code below:

```C#
var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
```


## Example Host

*Check the "Plugin" directory of this repository. It contains a fully functional demo plugin*

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

### Property: Priority

This property takes a value from `0x0` to `0xFFFF`. The default is `0xFEFF`

When all hosts have been loaded they will be sorted in accordance to that value,
with the lowest value being first in the request pipeline.

Values do not need to be unique, but the ordering of hosts with identical values will be indeterminate.
The ordering will be identical for as long as no plugins are added or removed.

Note: This property is evaluated after `Start()` has been called.
This means you're safe to provide a way for users to configure the priority of your host,
and set it during the call to `Start()` rather than the constructor.

Changing this value after the call to `Start()` has completed has no effect right now,
but upon popular demand might do so in the future.

#### Recommended Ranges

All internal hosts have at least `0xFF00` as the priority.
Using `0xFEFF` or lower will guarantee your host comes before any internal host.
Internal hosts do not use `0xFFFF`.
This permits custom fallback plugins to run.

Hosts with less than `0x1000` should be global access filters
that don't process requests but merely filter them (see Rewrite chapter below).
With faster filters using lower numbers, and slower filters higher numbers.

Hosts at `0x1000` up to `0x7FFF` should be hosts that rewrite URLs but do not otherwise process requests.

Hosts at `0x8000` up to `0xFEFF` should be regular hosts that process requests,
with hosts that are very fast tending towards the lower end of the range,
while slower hosts use the upper end of the range.

### Method: IsAccepted

This method decides whether a request is accepted or not.
If it returns `false`, the host will be skipped, but request processing continues with other hosts.
If it returns `true`, `Rewrite(...)` and `Request(...)` will be invoked shortly afterwards.

Note: This function has a default implementation that accepts all requests.
If this is what you want, you don't have to override this.

### Method: Rewrite

This method gets passed the current request url, and should return a url.

The returned url is used for all subsequent function calls in the request pipeline,
including other hosts. You may return null to abort the request without sending any response.
To implement a more graceful abort, do not return null here,
and return the appropriate response in `Request` instead.

Note: By overriding `Request` to always return null,
the host can effectively be turned into a pure url rewrite middleware.

### Method: Request

This method is responsible to process the request and return an adequate response.
You may return null to skip the request and continue with the request pipeline.
Returning a non-null value returns said value to the client and ends the request pipeline.

Implementing this method is required, as there is no default implementation in the base class.
If you purely want to work with `Rewrite`, you can hardcode this method to return null.

### Method: Start

This method is called at least once before the host is added to the request pipeline.
This is the appropriate location to perform long running initialization tasks.

### Method: Stop

This method is called at least once when it's shut down.
This method is appropriate to clean up objects that consume a lot of memory.

### Method: Dispose

This is called as the last method in the lifecycle of the host.
No other function will be called after this,
but this may be called multiple times.

This method is an appropriate location to clean up unmanaged resources.

## Creating Plugins

**The plugin system is still under development,**
**and the information below does not represent the current state of the server.**

To create a plugin, you want to zip at least two files.

The first file is your plugin dll in the output directory.
It's usually named identically to the project.
Rename this file to `Plugin.dll` before zipping it,
or rename it inside of the zip file afterwards.

The second file is an `info.json` with the following content:

```json
{
	"Id": "VVVVVVVV-WWWW-XXXX-YYYY-ZZZZZZZZZZZZ",
	"Version": "0.0.1",
	"Preserve": ["config.json"]
}
```

### Field: Id

This is the plugin id, and must be a valid guid. It's used as the directory name of the plugin,
and as such, must be unique to every plugin,
but not change between versions.

Visual Studio has a "Generate GUID" option,
or you can simply run `Console.WriteLine(Guid.NewGuid());` in a test application to generate a guid.

Using the nullguid `00000000-0000-0000-0000-000000000000` is not permitted

### Field: Version

This is the version of the plugin, preferrably in `a.b.c` format,
but any format accepted by `Version.Parse(str)` is fine.

Please note that the .NET version object does a pedantic compare.
Version `2.0` is not the same as version `2.0.0`,
the latter being considered newer.

### Field: Preserve

This is a list of files and directories you want to preserve when the plugin is updated.
The list is relative to the plugin directory and must not start with a directory separator character.
You usually want to use this to preserve configuration files during updates.

Note: During an update, this field is read from the info.json in the zip file,
not the already installed version.
Because of this, you don't need do know in advance which files you want to preserve
before you create your first update.

This field is optional.

## Plugin references

If your plugin contains references other than those from the .NET framework,
for example those referenced via Nuget, you want to add them to the zip file too.

You don't need to add `Gemini.Lib.dll` or any of its dependencies to the zip.

## Testing your plugin

It's highly recommended that you retain all published versions of the plugin.
When you release a new version,
you can test if it upgrades correctly from all previously released versions.

## Installing a Plugin

To install a plugin,
run the server executable with the arguments `/install`
as well as the zip file path of your plugin.

### Manual Installation

You can manually install a plugin if you want to.
The process is as follows:

1. Create the "Plugins" folder if it doesn't exists yet
2. Create a folder that matches the "Id" from the `info.json` file inside of the "Plugins" directory.
3. Extract the zip into the newly created folder.

## Updating a Plugin

Updating a plugin is the same procedure as installing it.
This will only work if the "Version" from the info.json file is newer than the old one.
The "Name" must also match the old one exactly

## Deleting a plugin

To delete a plugin,
run the server executable with the arguments `/delete` and the plugin id.

Note: Deleting a plugin will not preserve any files inside of the plugin directory.
If you want to update a plugin you can just install the new version over the old one.
