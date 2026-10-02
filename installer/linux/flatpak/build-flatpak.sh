#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
VERSION="${1:-1.0.0}"
OUTPUT_FILE="${2:-${REPO_ROOT}/dist/FileFlow-${VERSION}-x86_64.flatpak}"
SOURCE_PAYLOAD="${3}"

echo -e "\033[0;36m==========================================================\033[0m"
echo -e "\033[0;36m  FileFlow Studio - Compilador de Paquete Flatpak         \033[0m"
echo -e "\033[0;36m==========================================================\033[0m"

if ! command -v flatpak-builder &> /dev/null; then
    echo -e "\033[0;31m[ERROR] 'flatpak-builder' no está instalado. Instálalo con: sudo apt install flatpak-builder\033[0m"
    exit 1
fi

STAGE_DIR="/tmp/fileflow-flatpak-stage"
BUILD_DIR="/tmp/fileflow-flatpak-build"
REPO_DIR="/tmp/fileflow-flatpak-repo"
rm -rf "${STAGE_DIR}" "${BUILD_DIR}" "${REPO_DIR}"
mkdir -p "${STAGE_DIR}/payload" "$(dirname "${OUTPUT_FILE}")"
OUTPUT_DIR="$(cd "$(dirname "${OUTPUT_FILE}")" && pwd)"
OUTPUT_FILE_NAME="$(basename "${OUTPUT_FILE}")"
OUTPUT_FILE="${OUTPUT_DIR}/${OUTPUT_FILE_NAME}"

# 1. Preparar payload de binarios
if [ -n "${SOURCE_PAYLOAD}" ] && [ -d "${SOURCE_PAYLOAD}" ] && ( [ -f "${SOURCE_PAYLOAD}/FileFlow.App" ] || [ -f "${SOURCE_PAYLOAD}/FileFlow.App.Uno" ] ); then
    echo -e "\n\033[0;33m[1/3] Utilizando payload pre-publicado desde: ${SOURCE_PAYLOAD}...\033[0m"
    cp -r "${SOURCE_PAYLOAD}/"* "${STAGE_DIR}/payload/"
else
    echo -e "\n\033[0;33m[1/3] Publicando FileFlow Studio para linux-x64 (Self-Contained)...\033[0m"
    dotnet publish "${REPO_ROOT}/FileFlow.App.Uno/FileFlow.App.Uno.csproj" \
        -c Release \
        -p:FileFlowTarget=desktop \
        -r linux-x64 \
        --self-contained true \
        -p:DebugType=none \
        -p:DebugSymbols=false \
        -o "${STAGE_DIR}/payload"
fi

# Copiar archivos de metadata a la carpeta de staging
cp "${SCRIPT_DIR}/com.fileflowstudio.FileFlow.desktop" "${STAGE_DIR}/"
cp "${SCRIPT_DIR}/com.fileflowstudio.FileFlow.metainfo.xml" "${STAGE_DIR}/"
cp "${SCRIPT_DIR}/com.fileflowstudio.FileFlow.yml" "${STAGE_DIR}/"
cp "${REPO_ROOT}/assets/FileFlow.png" "${STAGE_DIR}/"

echo -e "\n\033[0;33m[2/3] Integrando en sandbox de Flatpak...\033[0m"
flatpak-builder \
    --force-clean \
    --repo="${REPO_DIR}" \
    --install-deps-from=flathub \
    --default-branch=stable \
    "${BUILD_DIR}" \
    "${STAGE_DIR}/com.fileflowstudio.FileFlow.yml"

echo -e "\n\033[0;33m[3/3] Exportando bundle .flatpak autónomo...\033[0m"
flatpak build-bundle \
    "${REPO_DIR}" \
    "${OUTPUT_FILE}" \
    com.fileflowstudio.FileFlow \
    stable

rm -rf "${STAGE_DIR}" "${BUILD_DIR}" "${REPO_DIR}"

if [ -f "${OUTPUT_FILE}" ]; then
    SIZE=$(du -h "${OUTPUT_FILE}" | cut -f1)
    echo -e "\n\033[0;32m[OK] Paquete Flatpak generado con éxito: ${OUTPUT_FILE} (${SIZE})\033[0m"
else
    echo -e "\n\033[0;31m[ERROR] No se pudo generar el archivo .flatpak\033[0m"
    exit 1
fi
