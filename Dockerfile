FROM ubuntu:22.04

# Microsoft'un resmi paket havuzundan .NET 8 SDK'yı kurabilmek için gerekli araçlar
RUN apt-get update && apt-get install -y \
    wget \
    unzip \
    libgl1 \
    libxi6 \
    libfontconfig1 \
    software-properties-common \
    gnupg \
    && rm -rf /var/lib/apt/lists/*

# Microsoft .NET 8.0 SDK'yı Ubuntu 22.04'e resmi olarak ekliyoruz
RUN wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb

# Godot ve .NET 8'i yüklüyoruz
RUN apt-get update && apt-get install -y \
    dotnet-sdk-8.0 \
    && rm -rf /var/lib/apt/lists/*

# Godot 4.6.2 C# (Mono) Linux standart sürümünü indiriyoruz
RUN wget https://github.com/godotengine/godot/releases/download/4.6.2-stable/Godot_v4.6.2-stable_mono_linux_x86_64.zip \
    && unzip Godot_v4.6.2-stable_mono_linux_x86_64.zip \
    && mv Godot_v4.6.2-stable_mono_linux_x86_64 /usr/local/bin/godot_mono \
    && rm Godot_v4.6.2-stable_mono_linux_x86_64.zip

# Proje dosyalarını kopyalıyoruz
COPY . /app
WORKDIR /app

# Standart mono binary dosyasını headless ve server modunda başlatıyoruz
CMD ["/usr/local/bin/godot_mono/Godot_v4.6.2-stable_mono_linux.x86_64", "--headless", "--server"]