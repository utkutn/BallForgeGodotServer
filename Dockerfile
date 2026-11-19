FROM ubuntu:22.04

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    GODOT_HOME=/usr/local/bin/godot_mono

# Gerekli sistem paketleri
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    wget \
    unzip \
    libgl1 \
    libxi6 \
    libfontconfig1 \
    libicu70 \
    && rm -rf /var/lib/apt/lists/*

# Microsoft .NET 8 SDK deposu ve kurulumu
RUN wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && rm -f packages-microsoft-prod.deb \
    && apt-get update && apt-get install -y --no-install-recommends \
    dotnet-sdk-8.0 \
    && rm -rf /var/lib/apt/lists/*

# Godot 4.7.2 Mono kurulumu
RUN wget -q https://github.com/godotengine/godot/releases/download/4.7.2-stable/Godot_v4.7.2-stable_mono_linux_x86_64.zip \
    && unzip -q Godot_v4.7.2-stable_mono_linux_x86_64.zip \
    && mv Godot_v4.7.2-stable_mono_linux_x86_64 "${GODOT_HOME}" \
    && chmod +x "${GODOT_HOME}/Godot_v4.7.2-stable_mono_linux.x86_64" \
    && rm -f Godot_v4.7.2-stable_mono_linux_x86_64.zip

WORKDIR /app
COPY . /app

# C# projesi varsa önceden derle
RUN if ls *.sln >/dev/null 2>&1 || ls *.csproj >/dev/null 2>&1; then \
        dotnet build -c Release; \
    fi

# Headless authoritative server
CMD ["/usr/local/bin/godot_mono/Godot_v4.7.2-stable_mono_linux.x86_64", "--headless", "--server"]