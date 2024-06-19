# massurler

I had a need to download a bunch of URLs (manually extracted from Web pages).
So I made this utility to automate that process a bit.

### Building

Usual dotnet project:

```bash
dotnet build Massurler\Massurler.fsproj -c Release
```

### Usage

```bash
USAGE: massurler [--help] --maxparalleldownloads <int> [--timeoutseconds <int>]

OPTIONS:

    --maxparalleldownloads, --maxp <int>
                          Specifies maximum number of parallel downloads.
    --timeoutseconds <int>
                          Specifies connection timeout in seconds. Default 30.
    --help                display this list of options.
```
After you've started utility type `help` to see menu commands.
