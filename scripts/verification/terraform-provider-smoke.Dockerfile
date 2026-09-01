FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:940f919ae84dd92ccd4aab7686fa5b777870b006c9360351039e16bcaad73d89

# These tools generate and verify the signed fabricated provider fixture. This
# image is used only by the portable certification harness, never released.
# The SDK base is digest-pinned and this Alpine v3.23 tool set is pinned to the
# exact package revisions recorded in docs/build-inputs.md. Keep this list in
# sync with that record so a certificate rerun cannot silently pick up a newer
# helper tool.
RUN apk add --no-cache \
    bash=5.3.3-r1 \
    gnupg=2.4.9-r0 \
    openssl=3.5.8-r0 \
    python3=3.12.14-r0

RUN mkdir -p /tmp/nuget /tmp/smoke-home \
    && chown -R app:app /tmp/nuget /tmp/smoke-home
ENV HOME=/tmp/smoke-home \
    NUGET_PACKAGES=/tmp/nuget
WORKDIR /src
COPY --chown=app:app . /src
USER app
