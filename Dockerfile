# Build aşaması
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Proje dosyalarını kopyala ve restore yap
COPY *.csproj ./
RUN dotnet restore

# Geri kalan dosyaları kopyala ve publish et
COPY . ./
RUN dotnet publish -c Release -r linux-x64 --self-contained false -o /app

# Runtime aşaması
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# OpenCV bağımlılıklarını kur
RUN apt-get update && apt-get install -y \
    libopencv-dev \
    libgdiplus \
    libc6-dev \
    libsm6 \
    libxext6 \
    libxrender-dev \
    libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

# Build edilen dosyaları kopyala
COPY --from=build /app .

# Uygulamayı başlat
ENTRYPOINT ["dotnet", "test.dll"]
