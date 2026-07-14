# Release candidate evidence

This file is deliberately a required-input record, not a certification claim.
Populate the values from the release candidate after the candidate image is built
and the final verification run completes. The validator rejects empty or
unrecognised fields, while `REQUIRED` truthfully denotes work that has not yet
been performed.

| Field | Value |
|---|---|
| Candidate image digest | REQUIRED |
| Candidate revision | REQUIRED |
| Verification run URL | REQUIRED |
| Terraform backend matrix result | REQUIRED |
| Fault and load result | REQUIRED |
| Operability gate result | REQUIRED |

Release certification is **pending** until every `REQUIRED` value is replaced
with a concrete, reviewed record by the final certification change.
