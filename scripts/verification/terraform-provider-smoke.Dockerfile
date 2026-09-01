FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:620e765fe18186c08399f7aa978f79f04b6bbf0ee1b3b8a91e2d5c9619e59da1

# These tools generate and verify the signed fabricated provider fixture. This
# image is used only by the portable certification harness, never released.
# The SDK base is digest-pinned and this Alpine v3.24 tool set is pinned to the
# exact package revisions recorded in docs/build-inputs.md. Keep this list in
# sync with that record so a certificate rerun cannot silently pick up a newer
# helper tool.
RUN apk add --no-cache \
    bash=5.3.9-r1 \
    gnupg=2.4.9-r1 \
    openssl=3.5.8-r0 \
    python3=3.14.7-r1

RUN mkdir -p /tmp/nuget /tmp/smoke-home \
    && chown -R app:app /tmp/nuget /tmp/smoke-home
ENV HOME=/tmp/smoke-home \
    NUGET_PACKAGES=/tmp/nuget
WORKDIR /src
COPY --chown=app:app . /src
USER app
