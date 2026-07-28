FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:d8ee39817ca03a3757288e83c37ed73cc969a286c603b827c7cbe33add1c2d1c

# These tools generate and verify the signed fabricated provider fixture. This
# image is used only by the portable certification harness, never released.
# The SDK base is digest-pinned and this Alpine v3.23 tool set is pinned to the
# exact package revisions recorded in docs/build-inputs.md. Keep this list in
# sync with that record so a certificate rerun cannot silently pick up a newer
# helper tool.
RUN apk add --no-cache \
    bash=5.3.3-r1 \
    gnupg=2.4.9-r0 \
    openssl=3.5.7-r0 \
    python3=3.12.13-r0

RUN mkdir -p /tmp/nuget /tmp/smoke-home \
    && chown -R app:app /tmp/nuget /tmp/smoke-home
ENV HOME=/tmp/smoke-home \
    NUGET_PACKAGES=/tmp/nuget
WORKDIR /src
COPY --chown=app:app . /src
USER app
