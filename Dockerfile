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

# Minimal gerekli paketler (EmguCV için)
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libc6-dev \
    libopencv-dev \
    libopencv-contrib-dev \
    libgomp1 \
    wget \
    && rm -rf /var/lib/apt/lists/*

# Eğer GUI gerekirse (X11 forwarding için)
# RUN apt-get update && apt-get install -y \
#     libsm6 \
#     libxext6 \
#     libxrender-dev \
#     libfontconfig1 \
#     libgtk-3-0 \
#     && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Environment variables for EmguCV
ENV OPENCV_LOG_LEVEL=ERROR
ENV OPENCV_OPENCL_DEVICE=disabled
ENV OPENCV_DISABLE_EIGEN_TENSOR_SUPPORT=1
ENV LD_LIBRARY_PATH=/usr/lib/x86_64-linux-gnu:$LD_LIBRARY_PATH
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

EXPOSE 80
ENTRYPOINT ["dotnet", "test.dll"]