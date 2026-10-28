# =============================================================================
# Dockerfile — RAG HR Assistant
# Multi-stage build: Node (React) → .NET SDK (build) → .NET Runtime (run)
#
# LEARNING NOTE — Why multi-stage?
# If we used a single stage, the final image would contain the .NET SDK,
# Node.js, npm, and all build tools — easily 2–3 GB.
# Multi-stage copies only the compiled output into the final runtime image,
# which ends up around 200 MB. This is standard practice for production images.
# =============================================================================


# -----------------------------------------------------------------------------
# Stage 1 — Build the React frontend
# We use Node to run Vite and produce the static files for wwwroot/
# -----------------------------------------------------------------------------
FROM node:22-alpine AS frontend

WORKDIR /app

# Copy package files first — Docker caches this layer.
# If package.json hasn't changed, npm install is skipped on rebuilds.
# This is a key Docker optimisation: put things that change rarely at the top.
COPY ClientApp/package*.json ClientApp/
RUN cd ClientApp && npm ci

# Now copy the rest of the source and build
COPY ClientApp/ ClientApp/
RUN cd ClientApp && npm run build
# Output lands at /app/wwwroot (vite.config.ts outDir: '../wwwroot')


# -----------------------------------------------------------------------------
# Stage 2 — Build the .NET API
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

# Restore dependencies first (cached layer — only reruns if .csproj changes)
COPY *.csproj ./
RUN dotnet restore

# Copy source code
COPY . .

# Copy the built React files from Stage 1 into wwwroot
COPY --from=frontend /app/wwwroot ./wwwroot

# Publish in Release mode — optimised, no debug symbols
RUN dotnet publish -c Release -o /app/publish --no-restore


# -----------------------------------------------------------------------------
# Stage 3 — Runtime image (the final, small image that actually runs)
# Uses the ASP.NET runtime — no SDK, no Node, just what's needed to run
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app

# Copy only the published output from Stage 2
COPY --from=build /app/publish .

# ASP.NET Core listens on 8080 by default in containers (not 5011)
EXPOSE 8080

# ASPNETCORE_ENVIRONMENT controls which appsettings file is loaded.
# We set it to Production so appsettings.Development.json is NOT loaded —
# secrets come from environment variables instead.
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "RagApi.dll"]
