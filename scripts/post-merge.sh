#!/bin/bash
set -e

echo "==> Restoring NuGet packages..."
dotnet restore LFPortal.sln

echo "==> Building solution..."
dotnet build LFPortal.sln --no-restore

echo "==> Post-merge setup complete."
