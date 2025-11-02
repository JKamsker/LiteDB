```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.6899)
Unknown processor
.NET SDK 9.0.306
  [Host]     : .NET 8.0.21 (8.0.2125.47513), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-ACDQAM : .NET 8.0.21 (8.0.2125.47513), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  ShortRun   : .NET 8.0.21 (8.0.2125.47513), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Jit=RyuJit  Runtime=.NET 8.0  Force=True  

```
| Method                       | Job        | Toolchain | IterationCount | LaunchCount | WarmupCount | N     | Mean         | Error         | StdDev       | Median       | Gen0     | Gen1     | Gen2     | Allocated  |
|----------------------------- |----------- |---------- |--------------- |------------ |------------ |------ |-------------:|--------------:|-------------:|-------------:|---------:|---------:|---------:|-----------:|
| **DataGeneration**               | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **10**    |     **11.08 μs** |      **0.221 μs** |     **0.403 μs** |     **10.97 μs** |   **0.6104** |   **0.0153** |        **-** |    **9.99 KB** |
| DataWithExclusionsGeneration | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10    |     11.72 μs |      0.240 μs |     0.689 μs |     11.55 μs |   0.6104 |   0.0153 |        - |    9.99 KB |
| DataGeneration               | ShortRun   | Default   | 3              | 1           | 3           | 10    |     11.64 μs |     15.392 μs |     0.844 μs |     11.52 μs |   0.6104 |   0.0153 |        - |    9.99 KB |
| DataWithExclusionsGeneration | ShortRun   | Default   | 3              | 1           | 3           | 10    |     11.18 μs |      8.987 μs |     0.493 μs |     10.90 μs |   0.6104 |   0.0153 |        - |    9.99 KB |
| **DataGeneration**               | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **50**    |     **53.86 μs** |      **1.073 μs** |     **1.357 μs** |     **53.91 μs** |   **2.9297** |   **0.3662** |        **-** |   **48.29 KB** |
| DataWithExclusionsGeneration | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 50    |     55.01 μs |      1.097 μs |     2.690 μs |     54.36 μs |   2.9297 |   0.3662 |        - |   48.29 KB |
| DataGeneration               | ShortRun   | Default   | 3              | 1           | 3           | 50    |     65.85 μs |    126.760 μs |     6.948 μs |     62.37 μs |   2.9297 |   0.3662 |        - |   48.29 KB |
| DataWithExclusionsGeneration | ShortRun   | Default   | 3              | 1           | 3           | 50    |     54.04 μs |     15.849 μs |     0.869 μs |     53.98 μs |   2.9297 |   0.3662 |        - |   48.29 KB |
| **DataGeneration**               | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **100**   |    **105.97 μs** |      **1.900 μs** |     **1.685 μs** |    **106.47 μs** |   **5.8594** |   **1.4648** |        **-** |   **96.19 KB** |
| DataWithExclusionsGeneration | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 100   |    108.68 μs |      2.119 μs |     3.821 μs |    108.45 μs |   5.8594 |   1.4648 |        - |   96.19 KB |
| DataGeneration               | ShortRun   | Default   | 3              | 1           | 3           | 100   |    113.61 μs |     94.515 μs |     5.181 μs |    112.81 μs |   5.8594 |   1.4648 |        - |   96.19 KB |
| DataWithExclusionsGeneration | ShortRun   | Default   | 3              | 1           | 3           | 100   |    107.67 μs |     49.786 μs |     2.729 μs |    107.66 μs |   5.8594 |   1.4648 |        - |   96.19 KB |
| **DataGeneration**               | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **500**   |    **525.34 μs** |      **8.598 μs** |     **9.556 μs** |    **526.11 μs** |  **28.3203** |  **18.5547** |        **-** |  **476.02 KB** |
| DataWithExclusionsGeneration | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 500   |    555.40 μs |     13.046 μs |    37.431 μs |    541.88 μs |  28.3203 |  18.5547 |        - |  476.02 KB |
| DataGeneration               | ShortRun   | Default   | 3              | 1           | 3           | 500   |    554.78 μs |    958.073 μs |    52.515 μs |    537.54 μs |  28.3203 |  18.5547 |        - |  476.02 KB |
| DataWithExclusionsGeneration | ShortRun   | Default   | 3              | 1           | 3           | 500   |    524.39 μs |    292.037 μs |    16.008 μs |    522.35 μs |  28.3203 |  18.5547 |        - |  476.02 KB |
| **DataGeneration**               | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **1000**  |  **1,094.75 μs** |     **26.912 μs** |    **75.464 μs** |  **1,067.76 μs** |  **56.6406** |  **44.9219** |        **-** |  **951.48 KB** |
| DataWithExclusionsGeneration | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 1000  |  1,069.89 μs |     20.064 μs |    32.966 μs |  1,062.16 μs |  56.6406 |  44.9219 |        - |  951.48 KB |
| DataGeneration               | ShortRun   | Default   | 3              | 1           | 3           | 1000  |  1,040.69 μs |    192.898 μs |    10.573 μs |  1,036.53 μs |  56.6406 |  44.9219 |        - |  951.48 KB |
| DataWithExclusionsGeneration | ShortRun   | Default   | 3              | 1           | 3           | 1000  |  1,055.76 μs |    257.674 μs |    14.124 μs |  1,047.67 μs |  56.6406 |  44.9219 |        - |  951.48 KB |
| **DataGeneration**               | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **5000**  |  **5,510.57 μs** |    **108.668 μs** |   **224.419 μs** |  **5,443.02 μs** | **289.0625** | **281.2500** |        **-** | **4808.02 KB** |
| DataWithExclusionsGeneration | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 5000  |  5,474.03 μs |    104.255 μs |   193.243 μs |  5,468.07 μs | 289.0625 | 281.2500 |        - | 4808.02 KB |
| DataGeneration               | ShortRun   | Default   | 3              | 1           | 3           | 5000  |  5,744.79 μs | 11,049.649 μs |   605.669 μs |  5,478.43 μs | 289.0625 | 281.2500 |        - | 4808.02 KB |
| DataWithExclusionsGeneration | ShortRun   | Default   | 3              | 1           | 3           | 5000  |  5,401.61 μs |    687.336 μs |    37.675 μs |  5,394.95 μs | 289.0625 | 281.2500 |        - | 4808.02 KB |
| **DataGeneration**               | **Job-ACDQAM** | **.NET 8.0**  | **Default**        | **Default**     | **Default**     | **10000** | **15,405.76 μs** |    **360.907 μs** | **1,041.298 μs** | **15,152.64 μs** | **734.3750** | **718.7500** | **281.2500** | **9611.88 KB** |
| DataWithExclusionsGeneration | Job-ACDQAM | .NET 8.0  | Default        | Default     | Default     | 10000 | 15,224.61 μs |    322.433 μs |   930.293 μs | 14,929.97 μs | 734.3750 | 718.7500 | 281.2500 | 9611.78 KB |
| DataGeneration               | ShortRun   | Default   | 3              | 1           | 3           | 10000 | 15,044.61 μs | 11,867.149 μs |   650.478 μs | 15,227.92 μs | 734.3750 | 718.7500 | 281.2500 | 9611.78 KB |
| DataWithExclusionsGeneration | ShortRun   | Default   | 3              | 1           | 3           | 10000 | 15,515.47 μs | 16,881.662 μs |   925.341 μs | 15,468.97 μs | 718.7500 | 687.5000 | 250.0000 | 9611.95 KB |
