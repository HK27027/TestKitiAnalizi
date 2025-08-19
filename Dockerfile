FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Minimal dependencies (sadece gerekli olanlar)
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libc6-dev \
    libopencv-dev \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Headless mode için environment variables
ENV OPENCV_LOG_LEVEL=ERROR
ENV OPENCV_OPENCL_DEVICE=disabled
ENV OPENCV_DISABLE_EIGEN_TENSOR_SUPPORT=1

EXPOSE 80
ENTRYPOINT ["dotnet", "test.dll"]