FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# EmguCV için gerekli sistem kütüphanelerini yükle
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libc6-dev \
    libx11-dev \
    libxft-dev \
    libxext-dev \
    libxrender-dev \
    libgtk-3-0 \
    libglib2.0-0 \
    libatk1.0-0 \
    libcairo-gobject2 \
    libgtk-3-0 \
    libgdk-pixbuf2.0-0 \
    libpango-1.0-0 \
    libharfbuzz0b \
    libfontconfig1 \
    libfreetype6 \
    && rm -rf /var/lib/apt/lists/*

# OpenCV native kütüphanelerini yükle
RUN apt-get update && apt-get install -y \
    libopencv-dev \
    libopencv-contrib-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Environment variables for EmguCV
ENV OPENCV_LOG_LEVEL=ERROR
ENV DISPLAY=:99

ENTRYPOINT ["dotnet", "test.dll"]