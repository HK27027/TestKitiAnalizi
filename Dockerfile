FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

# OpenCV ve libgdiplus bağımlılıklarını yükle
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libc6-dev \
    libsm6 \
    libxext6 \
    libxrender-dev \
    libglib2.0-0 \
    libopencv-core4.2 \
    libopencv-imgproc4.2 \
    libopencv-highgui4.2 \
    libopencv-videoio4.2 \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "test.dll"]
