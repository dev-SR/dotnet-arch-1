ARG PROJECT
ARG ASSEMBLY

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src ./src
RUN dotnet restore "$PROJECT"
RUN dotnet publish "$PROJECT" -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG ASSEMBLY
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
ENV ASSEMBLY=${ASSEMBLY}
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$ASSEMBLY\""]
