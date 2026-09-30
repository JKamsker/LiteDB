# Reusable Shared holder thread experiment

Empty native-holder workers are reused independently of close workers. Holder native ownership still ends after every transaction; the child SharedEngine and storage core are reconstructed. The pool retains at most two empty workers for one second; callbacks, execution contexts and test observers are cleared/restored before idle publication. Scheduler lifetime regressions are duplicated for this separate pool.

Baseline: c8c0cfab623a22880b1d71eb966b09e89f9153b9.

Cross-variant results and evidence are linked from JKamsker/LiteDB PR #133.
