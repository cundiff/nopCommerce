# Cloud Agent base image for nopCommerce: Ubuntu 24.04 + .NET 10 SDK + PostgreSQL.
# System-level, slow-changing dependencies live here; repository build and
# database seeding happen in .cursor/install.sh.
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive
# nopCommerce needs full globalization data (invariant mode breaks the installer).
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl gnupg sudo git locales tzdata \
        libicu74 libgdiplus libfontconfig1 \
        postgresql postgresql-contrib \
    && rm -rf /var/lib/apt/lists/*

# .NET 10 SDK from the Microsoft package feed.
RUN curl -sSL -o /tmp/packages-microsoft-prod.deb \
        https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
    && dpkg -i /tmp/packages-microsoft-prod.deb \
    && rm /tmp/packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends dotnet-sdk-10.0 \
    && rm -rf /var/lib/apt/lists/*

# The 'ubuntu' user (uid 1000) ships with the base image; give it passwordless
# sudo so setup scripts can manage the PostgreSQL service.
RUN echo "ubuntu ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/ubuntu \
    && chmod 0440 /etc/sudoers.d/ubuntu

USER ubuntu
