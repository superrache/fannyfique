FROM node:22-bookworm

# Cette image (non "slim") embarque déjà ca-certificates, git et curl, ce qui permet
# de basculer apt en HTTPS : le port 80 est bloqué sur certains réseaux.
#
# Outils ajoutés pour Fannyfique :
#  - libicu72 / libssl3 : dépendances natives du SDK .NET
#  - (ImageMagick, déjà présent dans l'image de base : génération des photos de test, tests/TestData/generate.sh)
#  - exiftool           : vérification des métadonnées EXIF/XMP des photos de test
#  - jq                 : inspection des fichiers fannyfique.json
RUN set -eux; \
    for f in /etc/apt/sources.list /etc/apt/sources.list.d/*; do \
      if [ -f "$f" ]; then sed -i 's|http://|https://|g' "$f"; fi; \
    done; \
    apt-get update; \
    apt-get install -y --no-install-recommends \
      git ca-certificates curl ripgrep openssh-client build-essential python3 \
      libicu72 libssl3 zlib1g \
      libimage-exiftool-perl jq unzip; \
    rm -rf /var/lib/apt/lists/*

# SDK .NET 8 (LTS), installé via le script officiel : il gère amd64 et arm64
# (le dépôt apt de Microsoft pour Debian ne fournit que amd64).
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    # Propriété MSBuild : permet de compiler les projets net8.0-windows (WPF) sous Linux.
    # Ils compilent mais ne s'exécutent pas ici ; les tests s'exécutent sur les projets net8.0.
    EnableWindowsTargeting=true
RUN set -eux; \
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh; \
    bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$DOTNET_ROOT"; \
    ln -s "$DOTNET_ROOT/dotnet" /usr/local/bin/dotnet; \
    rm /tmp/dotnet-install.sh; \
    dotnet --info

# Claude Code (version figée possible : @anthropic-ai/claude-code@x.y.z)
RUN npm install -g @anthropic-ai/claude-code

# Utilisateur non-root (fourni par l'image node)
USER node
WORKDIR /workspace

CMD ["bash"]
