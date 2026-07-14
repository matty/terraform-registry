# Release candidate evidence

This file is deliberately a required-input record, not a certification claim.
Populate the values from the release candidate after the candidate image is built
and the final verification run completes. The validator rejects empty or
unrecognised fields, while `REQUIRED` truthfully denotes work that has not yet
been performed.

| Field | Value |
|---|---|
| Candidate image digest | sha256:953d2d1aa6b96b51ef85a826bff8fe57bf0110815d2c4b894e960fea8303e3a6 |
| Candidate revision | c4997fc59140d9f2903552285cb1ccc94a3c9536 |
| Verification run URL | https://github.com/matty/terraform-registry/actions/runs/29345431401 |
| Terraform backend matrix result | PASS |
| Fault and load result | PASS |
| Operability gate result | PASS |

The candidate artifact from the recorded run binds
`c4997fc59140d9f2903552285cb1ccc94a3c9536` to
`refs/heads/release/candidate-2026-07-14` before upload. The image was
published by [CI run 29339773534](https://github.com/matty/terraform-registry/actions/runs/29339773534);
its OCI `org.opencontainers.image.revision` label resolves to the recorded
candidate revision.

Release certification is **complete** for this candidate's automated evidence.
