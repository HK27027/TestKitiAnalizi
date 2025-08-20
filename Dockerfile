FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Tüm dosyaları kopyala ve build et
COPY . .
RUN dotnet clean
RUN dotnet restore --force
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# EmguCV ve System.Drawing için gerekli kütüphaneler
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libc6-dev \
    libopencv-dev \
    libgomp1 \
    libsm6 \
    libxext6 \
    libxrender-dev \
    libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Environment variables for EmguCV
ENV OPENCV_LOG_LEVEL=ERROR
ENV OPENCV_OPENCL_DEVICE=disabled
ENV OPENCV_DISABLE_EIGEN_TENSOR_SUPPORT=1

EXPOSE 80

# Burada kendi projenin DLL adını yaz (örnek: MyApp.dll)
ENTRYPOINT ["dotnet", "MyApp.dll"]
