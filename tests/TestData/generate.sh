#!/usr/bin/env bash
# Génère une arborescence de démonstration simulant plusieurs sources (CLAUDE.md 10) :
# carte SD, PC, NAS, disque externe, avec des lots complets, incomplets, divergents,
# sans numéro de série, illisibles, deux boîtiers de même modèle, et un dossier de récupération
# de carte aux fichiers renommés (source « recup », à ajouter avec l'option « ignorer les noms »).
#
# Les « RAW » sont de petits TIFF avec métadonnées EXIF (écrites par exiftool), renommés en
# .CR2/.NEF : MetadataExtractor les lit comme des RAW basés sur TIFF.
#
# Usage : tests/TestData/generate.sh [dossier de sortie]   (défaut : tests/TestData/demo)
# Prérequis : ImageMagick (convert), exiftool.
set -euo pipefail

OUT="${1:-$(dirname "$0")/demo}"
command -v convert >/dev/null || { echo "ImageMagick (convert) est requis" >&2; exit 1; }
command -v exiftool >/dev/null || { echo "exiftool est requis" >&2; exit 1; }

rm -rf "$OUT"
mkdir -p "$OUT"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
ARGS="$WORK/exiftool.args"
: > "$ARGS"

# Images de base de tailles différentes (le contenu importe peu).
convert -size 320x200 plasma:fractal -depth 8 -compress none "$WORK/base.tif"
convert -size 160x100 gradient:blue-pink -depth 8 -compress none "$WORK/small.tif"

# raw <fichier> <date "AAAA:MM:JJ HH:MM:SS"> <marque> <modèle> <série|-> [base]
raw() {
  local file="$OUT/$1" date="$2" make="$3" model="$4" serial="$5" base="${6:-base}"
  mkdir -p "$(dirname "$file")"
  cp "$WORK/$base.tif" "$WORK/tmp.tif"
  local tif="$WORK/$(echo "$1" | tr '/' '_').tif"
  mv "$WORK/tmp.tif" "$tif"
  {
    echo "-overwrite_original"
    echo "-Make=$make"
    echo "-Model=$model"
    echo "-DateTimeOriginal=$date"
    [ "$serial" != "-" ] && echo "-SerialNumber=$serial"
    echo "$tif"
    echo "-execute"
  } >> "$ARGS"
  echo "$tif|$file" >> "$WORK/moves"
}

# copy <source> <destination> : copie à l'identique (après génération).
copy() { echo "$1|$2" >> "$WORK/copies"; }

# --- Carte SD « EOS_DIGITAL » : 2 dossiers, boîtier A (Canon R6, série 0331) ---
for i in $(seq -f %02g 1 30); do
  raw "sd/DCIM/100CANON/IMG_00$i.CR2" "2024:05:12 14:$i:00" Canon "Canon EOS R6" 033100000001
done
for i in $(seq -f %02g 31 40); do
  raw "sd/DCIM/101CANON/IMG_00$i.CR2" "2024:06:02 10:$i:00" Canon "Canon EOS R6" 033100000001
done

# Rafale : deux photos à la même seconde, même boîtier, même taille → ambiguës une fois renommées.
for i in 01 02; do
  raw "sd/DCIM/102CANON/IMG_01$i.CR2" "2024:06:15 16:20:00" Canon "Canon EOS R6" 033100000001
done

# --- PC : copie complète du mariage + annexes JPEG/XMP ---
for i in $(seq -f %02g 1 30); do
  copy "sd/DCIM/100CANON/IMG_00$i.CR2" "pc/Photos/2024-05-12 Mariage Julie/IMG_00$i.CR2"
done
touch "$OUT/.keep"

# --- NAS : sélection du mariage (20 sur 30) + archives anciennes ---
for i in $(seq -f %02g 1 20); do
  copy "sd/DCIM/100CANON/IMG_00$i.CR2" "nas/Archives/2024/Mariage/IMG_00$i.CR2"
done
for i in $(seq -f %02g 1 15); do
  raw "nas/Archives/2023/Vacances/IMG_00$i.CR2" "2023:08:0$((10#$i % 9 + 1)) 09:$i:00" Canon "Canon EOS R6" 033100000001 small
done

# --- Disque externe : sauvegarde des vacances + 101CANON, dont 2 fichiers divergents ---
for i in $(seq -f %02g 1 15); do
  copy "nas/Archives/2023/Vacances/IMG_00$i.CR2" "externe/Sauvegarde/Vacances 2023/IMG_00$i.CR2"
done
for i in $(seq -f %02g 31 38); do
  copy "sd/DCIM/101CANON/IMG_00$i.CR2" "externe/Sauvegarde/Juin 2024/IMG_00$i.CR2"
done

# --- Deuxième boîtier de même modèle (série 0442) : mêmes noms, autres dates → pas des doublons ---
for i in $(seq -f %02g 1 5); do
  raw "pc/Photos/Boitier B/IMG_00$i.CR2" "2024:07:20 18:$i:00" Canon "Canon EOS R6" 044200000002 small
done

# --- Nikon sans numéro de série : identité probable ---
for i in $(seq -f %02g 1 6); do
  raw "pc/Photos/Nikon/DSC_00$i.NEF" "2022:03:15 11:$i:00" "NIKON CORPORATION" "NIKON D750" - small
  copy "pc/Photos/Nikon/DSC_00$i.NEF" "nas/Archives/2022/Nikon/DSC_00$i.NEF"
done

# --- Récupération de carte (PhotoRec) : noms perdus, séances réparties dans des recup_dir.N ---
for i in $(seq -f %02g 21 30); do
  copy "sd/DCIM/100CANON/IMG_00$i.CR2" "recup/recup_dir.1/f10$i.cr2"
done
for i in $(seq -f %02g 31 40); do
  copy "sd/DCIM/101CANON/IMG_00$i.CR2" "recup/recup_dir.2/f20$i.cr2"
done
for i in 01 02; do
  copy "sd/DCIM/102CANON/IMG_01$i.CR2" "recup/recup_dir.2/f30$i.cr2"
done

echo "Écriture des métadonnées…"
exiftool -q -q -@ "$ARGS" >/dev/null
while IFS='|' read -r tif file; do mv "$tif" "$file"; done < "$WORK/moves"
while IFS='|' read -r src dst; do mkdir -p "$(dirname "$OUT/$dst")"; cp -p "$OUT/$src" "$OUT/$dst"; done < "$WORK/copies"

# Copies divergentes : même photo, taille différente (fichier tronqué).
for i in 37 38; do
  f="$OUT/externe/Sauvegarde/Juin 2024/IMG_00$i.CR2"
  head -c $(( $(stat -c %s "$f") - 4096 )) "$f" > "$f.tmp" && mv "$f.tmp" "$f"
done

# Fichiers annexes et fichier illisible.
for i in 01 02 03; do echo "jpeg" > "$OUT/pc/Photos/2024-05-12 Mariage Julie/IMG_00$i.JPG"; done
echo "xmp" > "$OUT/pc/Photos/2024-05-12 Mariage Julie/IMG_0001.xmp"
head -c 50000 /dev/urandom > "$OUT/sd/DCIM/101CANON/IMG_0041.CR2"
mkdir -p "$OUT/sd/System Volume Information" && cp "$OUT/sd/DCIM/100CANON/IMG_0001.CR2" "$OUT/sd/System Volume Information/"
rm -f "$OUT/.keep"

echo "Arborescence générée dans $OUT :"
for d in sd pc nas externe recup; do
  printf '  %-8s %3d RAW\n' "$d" "$(find "$OUT/$d" -type f \( -iname '*.cr2' -o -iname '*.nef' \) -not -path '*System Volume*' | wc -l)"
done
