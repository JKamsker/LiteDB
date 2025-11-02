```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.6899)
Unknown processor
.NET SDK 9.0.306
  [Host]     : .NET 8.0.21 (8.0.2125.47513), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-ACDQAM : .NET 8.0.21 (8.0.2125.47513), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  ShortRun   : .NET 8.0.21 (8.0.2125.47513), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Jit=RyuJit  Runtime=.NET 8.0  Force=True  

```
| Method                      | Job        | Toolchain | IterationCount | LaunchCount | WarmupCount | DatasetSize | ConnectionType | Password       | Mean | Error |
|---------------------------- |----------- |---------- |--------------- |------------ |------------ |------------ |--------------- |--------------- |-----:|------:|
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **10**          | **Direct**         | **?**              |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10          | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10          | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10          | Direct         | ?              |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 10          | Direct         | ?              |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 10          | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 10          | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 10          | Direct         | ?              |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **10**          | **Direct**         | **SecurePassword** |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10          | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10          | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10          | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 10          | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 10          | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 10          | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 10          | Direct         | SecurePassword |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **50**          | **Direct**         | **?**              |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 50          | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 50          | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 50          | Direct         | ?              |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 50          | Direct         | ?              |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 50          | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 50          | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 50          | Direct         | ?              |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **50**          | **Direct**         | **SecurePassword** |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 50          | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 50          | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 50          | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 50          | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 50          | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 50          | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 50          | Direct         | SecurePassword |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **100**         | **Direct**         | **?**              |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 100         | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 100         | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 100         | Direct         | ?              |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 100         | Direct         | ?              |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 100         | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 100         | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 100         | Direct         | ?              |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **100**         | **Direct**         | **SecurePassword** |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 100         | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 100         | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 100         | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 100         | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 100         | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 100         | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 100         | Direct         | SecurePassword |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **500**         | **Direct**         | **?**              |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 500         | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 500         | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 500         | Direct         | ?              |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 500         | Direct         | ?              |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 500         | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 500         | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 500         | Direct         | ?              |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **500**         | **Direct**         | **SecurePassword** |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 500         | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 500         | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 500         | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 500         | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 500         | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 500         | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 500         | Direct         | SecurePassword |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **1000**        | **Direct**         | **?**              |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 1000        | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 1000        | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 1000        | Direct         | ?              |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 1000        | Direct         | ?              |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 1000        | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 1000        | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 1000        | Direct         | ?              |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **1000**        | **Direct**         | **SecurePassword** |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 1000        | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 1000        | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 1000        | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 1000        | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 1000        | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 1000        | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 1000        | Direct         | SecurePassword |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **5000**        | **Direct**         | **?**              |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 5000        | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 5000        | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 5000        | Direct         | ?              |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 5000        | Direct         | ?              |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 5000        | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 5000        | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 5000        | Direct         | ?              |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **5000**        | **Direct**         | **SecurePassword** |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 5000        | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 5000        | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 5000        | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 5000        | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 5000        | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 5000        | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 5000        | Direct         | SecurePassword |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **10000**       | **Direct**         | **?**              |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10000       | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10000       | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10000       | Direct         | ?              |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 10000       | Direct         | ?              |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 10000       | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 10000       | Direct         | ?              |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 10000       | Direct         | ?              |   NA |    NA |
| **WhereNear_Filter**            | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **10000**       | **Direct**         | **SecurePassword** |   **NA** |    **NA** |
| WhereNear_Filter_Indexed    | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10000       | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10000       | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10000       | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter            | ShortRun   | Default   | 3              | 1           | 3           | 10000       | Direct         | SecurePassword |   NA |    NA |
| WhereNear_Filter_Indexed    | ShortRun   | Default   | 3              | 1           | 3           | 10000       | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit         | ShortRun   | Default   | 3              | 1           | 3           | 10000       | Direct         | SecurePassword |   NA |    NA |
| TopKNear_OrderLimit_Indexed | ShortRun   | Default   | 3              | 1           | 3           | 10000       | Direct         | SecurePassword |   NA |    NA |

Benchmarks with issues:
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=50, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=50, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=50, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=50, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=50, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=50, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=50, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=50, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=50, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=50, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=50, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=50, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=50, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=50, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=50, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=50, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=100, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=100, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=100, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=100, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=100, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=100, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=100, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=100, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=100, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=100, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=100, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=100, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=100, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=100, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=100, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=100, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=500, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=500, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=500, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=500, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=500, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=500, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=500, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=500, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=500, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=500, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=500, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=500, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=500, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=500, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=500, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=500, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=1000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=1000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=1000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=1000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=1000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=1000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=1000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=1000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=1000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=1000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=1000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=1000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=1000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=1000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=1000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=1000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=5000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=5000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=5000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=5000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=5000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=5000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=5000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=5000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=5000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=5000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=5000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=5000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=5000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=5000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=5000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=5000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10000, ConnectionType=Direct, Password=?]
  QueryWithVectorSimilarity.WhereNear_Filter: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: Job-ACDQAM(Jit=RyuJit, Runtime=.NET 8.0, Force=True, Toolchain=.NET 8.0) [DatasetSize=10000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.WhereNear_Filter_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10000, ConnectionType=Direct, Password=SecurePassword]
  QueryWithVectorSimilarity.TopKNear_OrderLimit_Indexed: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DatasetSize=10000, ConnectionType=Direct, Password=SecurePassword]
