FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Tüm dosyaları kopyala ve tek seferde build et
COPY . .
RUN dotnet clean
RUN dotnet restore --force
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Sistem paketlerini tek RUN komutunda yükle
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libc6-dev \
    libopencv-dev \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Environment variables for EmguCV
ENV OPENCV_LOG_LEVEL=ERROR
ENV OPENCV_OPENCL_DEVICE=disabled
ENV OPENCV_DISABLE_EIGEN_TENSOR_SUPPORT=1

EXPOSE 80
ENTRYPOINT ["dotnet", "test.dll"]