# Compilation de `llama-server` par plateforme

Les binaires officiels sont récupérés par `scripts/fetch-binaries.sh`. Ils sont recommandés en production. Cette page documente la compilation locale, utile quand :

- La release upstream requiert une glibc trop récente (rencontré : b11071 nécessite glibc ≥ 2.38, or Debian 12 / Ubuntu 22.04 sont en 2.36).
- La cible n'est pas publiée (ex. Windows ARM64 aujourd'hui).
- Vous voulez un backend GPU non-livré par défaut.

## Prérequis communs

- `git`, `cmake` ≥ 3.16, un compilateur C++17 (`gcc` ≥ 11, `clang` ≥ 14, MSVC 2022).
- 2 Go RAM libre pour la compilation.
- ~4 min de build sur 8 cœurs pour la cible `llama-server` uniquement.

## Étapes (Linux/macOS)

```bash
git clone --depth 1 --branch b11071 https://github.com/ggml-org/llama.cpp.git
cd llama.cpp
cmake -B build \
    -DGGML_NATIVE=OFF \
    -DLLAMA_BUILD_TESTS=OFF \
    -DLLAMA_BUILD_EXAMPLES=OFF \
    -DLLAMA_BUILD_TOOLS=ON \
    -DCMAKE_BUILD_TYPE=Release
cmake --build build --target llama-server -j "$(nproc)"

# Copier binaire + libs vers bin/<plat>/cpu/
PLAT=linux-aarch64   # ou linux-x86_64, darwin-arm64, darwin-x86_64
mkdir -p ../bin/$PLAT/cpu
cp build/bin/llama-server ../bin/$PLAT/cpu/
cp -L build/bin/*.so* ../bin/$PLAT/cpu/ 2>/dev/null || \
    cp -L build/bin/*.dylib ../bin/$PLAT/cpu/
chmod +x ../bin/$PLAT/cpu/llama-server
```

## Étapes (Windows x86_64, PowerShell)

```powershell
git clone --depth 1 --branch b11071 https://github.com/ggml-org/llama.cpp.git
cd llama.cpp
cmake -B build -DGGML_NATIVE=OFF -DLLAMA_BUILD_TESTS=OFF `
    -DLLAMA_BUILD_EXAMPLES=OFF -DLLAMA_BUILD_TOOLS=ON `
    -DCMAKE_BUILD_TYPE=Release
cmake --build build --target llama-server --config Release -j 8

mkdir ..\bin\windows-x86_64\cpu -Force
copy build\bin\Release\llama-server.exe ..\bin\windows-x86_64\cpu\
copy build\bin\Release\*.dll ..\bin\windows-x86_64\cpu\
```

## Variantes GPU

Ajouter au `cmake` de configuration :

| Backend | Flag CMake | Prérequis |
|---|---|---|
| CUDA | `-DGGML_CUDA=ON` | CUDA Toolkit 12+ |
| ROCm | `-DGGML_HIP=ON` | ROCm 6+ |
| Vulkan | `-DGGML_VULKAN=ON` | Vulkan SDK 1.3+ |
| Metal (macOS) | `-DGGML_METAL=ON` | Xcode CLT |

Puis copier vers `bin/<plat>/<backend>/` (créer le sous-dossier `<backend>`).

## Cas glibc < 2.38

Symptôme : `/lib/aarch64-linux-gnu/libc.so.6: version GLIBC_2.38 not found`.

**Solution** : compiler en local sur la même famille de distribution que la cible (glibc 2.36 sur Debian 12 → binaire compatible glibc 2.36+). C'est exactement ce qui a été fait dans ce dépôt pour `bin/linux-aarch64/cpu/`.

Alternative : compiler avec `-static-libstdc++` (mais glibc reste dynamique).

## Vérification

```bash
./bin/linux-x86_64/cpu/llama-server --version
# → version: X (commit ...)
```

Puis ajouter le SHA256 dans `release.json` (`scripts/build-release.sh` sera fourni en Phase 4).
