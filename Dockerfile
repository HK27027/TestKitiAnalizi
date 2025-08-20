FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Sadece csproj dosyalarını kopyala ve restore et (cache için)
COPY *.sln .
COPY test/*.csproj ./test/
RUN dotnet restore

# Tüm kaynak kodlarını kopyala ve publish et
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Sistem paketlerini güncelle ve gerekli kütüphaneleri yükle
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libc6-dev \
    libopencv-dev \
    libopencv-contrib-dev \
    libgomp1 \
    libsm6 \
    libxext6 \
    libxrender-dev \
    libfontconfig1 \
    libgtk-3-0 \
    libdc1394-22 \
    libv4l-0 \
    libavcodec58 \
    libavformat58 \
    libswscale5 \
    libjpeg-dev \
    libpng-dev \
    libtiff-dev \
    libatlas-base-dev \
    gfortran \
    wget \
    && rm -rf /var/lib/apt/lists/*

# OpenCV shared libraries için symbolic link oluştur
RUN ln -s /usr/lib/x86_64-linux-gnu/libopencv_core.so.4.5 /usr/lib/x86_64-linux-gnu/libopencv_core.so.4 || true
RUN ln -s /usr/lib/x86_64-linux-gnu/libopencv_imgproc.so.4.5 /usr/lib/x86_64-linux-gnu/libopencv_imgproc.so.4 || true

WORKDIR /app
COPY --from=build /app .

# Environment variables for EmguCV
ENV OPENCV_LOG_LEVEL=ERROR
ENV OPENCV_OPENCL_DEVICE=disabled
ENV OPENCV_DISABLE_EIGEN_TENSOR_SUPPORT=1
ENV LD_LIBRARY_PATH=/usr/lib/x86_64-linux-gnu:$LD_LIBRARY_PATH

# EmguCV runtime'ı kontrol et
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

EXPOSE 80
ENTRYPOINT ["dotnet", "test.dll"]