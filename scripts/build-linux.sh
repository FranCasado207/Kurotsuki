#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

JAVA_HOME_DIR="${JAVA_HOME:-$(dirname "$(dirname "$(readlink -f "$(command -v java)")")")}"
VERSION="$(mvn -q help:evaluate -Dexpression=project.version -DforceStdout)"
APP_JAR="target/kurotsuki-${VERSION}.jar"
DIST="target/dist/kurotsuki"

mvn -q clean package -DskipTests dependency:copy-dependencies -DoutputDirectory=target/lib -DincludeScope=runtime

mkdir -p target/javafx
mv target/lib/javafx-*.jar target/javafx/

MODULES="$(jdeps --multi-release 25 --ignore-missing-deps --print-module-deps \
    --class-path "target/lib/*" "$APP_JAR" | tail -n 1)"
MODULES="$(printf '%s\n' "$MODULES" javafx.base javafx.graphics javafx.controls javafx.media \
    | tr ',' '\n' | grep -v '^$' | sort -u | paste -sd, -)"
echo "Modules: $MODULES"

rm -rf target/runtime
jlink --module-path "$JAVA_HOME_DIR/jmods:target/javafx" \
    --add-modules "$MODULES" \
    --strip-debug --no-header-files --no-man-pages --compress zip-6 \
    --output target/runtime

rm -rf "$DIST"
mkdir -p "$DIST/lib"
cp -r target/runtime "$DIST/runtime"
cp "$APP_JAR" target/lib/*.jar "$DIST/lib/"
cp scripts/kurotsuki.sh "$DIST/kurotsuki"
chmod +x "$DIST/kurotsuki"

echo "Build concluído: $DIST/kurotsuki"
