# Release candidate evidence

This file is deliberately a required-input record, not a certification claim.
Populate the values from the release candidate after the candidate image is built
and the final verification run completes. The validator rejects empty or
unrecognised fields, while `REQUIRED` truthfully denotes work that has not yet
been performed.

| Field | Value |
|---|---|
| Candidate image digest | sha256:914d6f8621fcc1e9b49dbf0a2d02f0a660cd660aaae6d5abeb52b2cbac23e4f2 |
| Candidate revision | 5c7702e702c2c0fbb9f2d27a0a3a52f50d746132 |
| Verification run URL | https://github.com/matty/terraform-registry/actions/runs/29359320935 |
| Terraform backend matrix result | PASS |
| Fault and load result | PASS |
| Operability gate result | PASS |

The candidate artifact from the recorded run binds
`5c7702e702c2c0fbb9f2d27a0a3a52f50d746132` to `refs/heads/develop`
before upload. The image was published by [CI run 29359320935](https://github.com/matty/terraform-registry/actions/runs/29359320935);
its OCI `org.opencontainers.image.revision` label resolves to the recorded
candidate revision.

Release certification is **complete** for this candidate's automated evidence.
