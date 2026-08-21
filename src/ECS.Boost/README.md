[![friflo ECS](https://raw.githubusercontent.com/friflo/Friflo.Engine.ECS/main/docs/images/friflo-ECS.svg)](https://github.com/friflo/Friflo.Engine.ECS)        ![splash](https://raw.githubusercontent.com/friflo/Friflo.Engine.ECS/main/docs/images/paint-splatter.svg)


[![Wiki](https://img.shields.io/badge/GitHub-grey?logo=github&logoColor=white)](https://github.com/vrimar/Friflo.Engine.ECS)
[![nuget](https://img.shields.io/nuget/v/CoFuse.Engine.ECS.Boost?logo=nuget&logoColor=white)](https://www.nuget.org/packages/CoFuse.Engine.ECS.Boost)
[![codecov](https://img.shields.io/codecov/c/gh/vrimar/Friflo.Engine.ECS/fuse?logo=codecov&logoColor=white&label=codecov)](https://app.codecov.io/gh/vrimar/Friflo.Engine.ECS/tree/fuse/src/ECS)
[![C# API](https://img.shields.io/badge/C%23%20API-22aaaa?logo=github&logoColor=white)](https://github.com/friflo/Friflo.Engine-docs)
[![Discord](https://img.shields.io/badge/Discord-5865F2?logo=discord&logoColor=white)](https://discord.gg/nFfrhgQkb8)
[![Wiki](https://img.shields.io/badge/Wiki-A200FF?logo=gitbook&logoColor=white)](https://friflo.gitbook.io/friflo.engine.ecs)


# CoFuse.Engine.ECS.Boost

Extension for [CoFuse.Engine.ECS](https://www.nuget.org/packages/CoFuse.Engine.ECS/) to boost performance of query execution.

**CoFuse.Engine.ECS** is using only **verifiably safe code**.  
Using [unsafe code](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code)
may lead to access violations errors in case of bugs caused by *unsafe code*.  

To improve performance of query execution this library is allowed to use unsafe code.  
For large query result sets the bounds check for array access using safe code is significant.

With unsafe code these bounds check can be elided resulting in a performance boost of ~30%.







