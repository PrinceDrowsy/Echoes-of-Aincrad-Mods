# Third-party notices

Cardinal is released under the MIT License. It is built on the libraries below, which are bundled inside `Cardinal.exe`. Their licenses allow this; their notices follow.

## CUE4Parse

Cardinal reads the game's files with [CUE4Parse](https://github.com/FabianFG/CUE4Parse) by FabianFG and contributors, licensed under the Apache License 2.0. The full license is in `licenses/CUE4Parse-LICENSE.txt`. CUE4Parse's NOTICE file, which credits the libraries it uses and includes their license texts, is reproduced unchanged in `licenses/CUE4Parse-NOTICE.txt`. Cardinal does not modify CUE4Parse.

Libraries credited in that NOTICE and bundled with Cardinal: Berrysoft.XXTea, Blake3, BouncyCastle.Cryptography, FixedMathSharp, Fmod5Sharp, GenericReader, Infrablack.UE4Config, K4os.Compression.LZ4, LZMA-SDK, Newtonsoft.Json, OffiUtils, Oodle.NET, Serilog, SubstreamSharp, VGAudio, Zlib-ng.NET and ZstdSharp.Port.

## UAssetAPI

Cardinal edits game assets for mods with [UAssetAPI](https://github.com/atenfyr/UAssetAPI) 1.1.0 by atenfyr, licensed under the MIT License (`licenses/UAssetAPI-LICENSE.txt`), bundled inside `Cardinal.exe`. Its dependencies, Newtonsoft.Json and ZstdSharp.Port, are already credited above.

## retoc

Cardinal builds mods with [retoc](https://github.com/trumank/retoc) by Truman Kilen and Archengius, licensed under the MIT License (`licenses/retoc-LICENSE.txt`). It ships unchanged as `retoc.exe` beside `Cardinal.exe`, which runs it as a separate program. The first time it needs Oodle, retoc downloads `oo2core_9_win64.dll` next to itself.

## Other libraries

| Library | License | Copyright |
|---|---|---|
| CommunityToolkit.HighPerformance | MIT | .NET Foundation and Contributors |
| IndexRange | MIT | Bradley Grainger |
| K4os.Hash.xxHash | MIT | Milosz Krajewski |
| MemoryPack | MIT | Cysharp, Inc. |
| NAudio.Core | MIT | Mark Heath and contributors |
| OggVorbisEncoder | MIT | Steve Lillis |
| OodleSharp | MIT | NotOfficer |
| Serilog.Sinks.Console | Apache-2.0 | Serilog Contributors |
| Microsoft.Bcl.Memory, System.IO.Hashing, System.Numerics.Tensors | MIT | .NET Foundation and Contributors |
| .NET runtime and WPF (self-contained) | MIT | .NET Foundation and Contributors |

The .NET runtime's own license and third-party notices are included as `licenses/dotnet-LICENSE.txt` and `licenses/dotnet-ThirdPartyNotices.txt`. The Apache License 2.0 text for Serilog.Sinks.Console is the same as in `licenses/CUE4Parse-LICENSE.txt`.

MIT License text, for the MIT-licensed libraries above:

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Not included

Cardinal ships no game files, no encryption key, no mappings file and no Oodle library. Cardinal's own Oodle library is either chosen by the player or downloaded on request from the [OodleUE](https://github.com/WorkingRobot/OodleUE) builds, through CUE4Parse's own downloader. retoc fetches its own copy, as described above.
