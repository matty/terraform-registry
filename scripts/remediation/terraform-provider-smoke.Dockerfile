FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:940f919ae84dd92ccd4aab7686fa5b777870b006c9360351039e16bcaad73d89

# These tools generate and verify the signed fabricated provider fixture. This
# image is used only by the portable certification harness, never released.
RUN apk add --no-cache bash gnupg openssl python3
