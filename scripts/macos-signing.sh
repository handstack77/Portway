#!/usr/bin/env bash
# 임시 CI 실행 환경용입니다. 인증서 암호를 명령줄 인수로 전달하지 마세요.
set -euo pipefail
: "${RUNNER_TEMP:?CI runner required}"
: "${PORTWAY_MAC_APP_P12:?Application certificate (base64) required}"
: "${PORTWAY_MAC_INSTALLER_P12:?Installer certificate (base64) required}"
: "${PORTWAY_MAC_P12_PASSWORD:?Certificate password required}"
: "${PORTWAY_MAC_KEYCHAIN_PASSWORD:?Temporary keychain password required}"
signing_dir="$RUNNER_TEMP/portway-signing"
mkdir -p "$signing_dir"
chmod 700 "$signing_dir"
printf '%s' "$PORTWAY_MAC_APP_P12" | base64 --decode > "$signing_dir/application.p12"
printf '%s' "$PORTWAY_MAC_INSTALLER_P12" | base64 --decode > "$signing_dir/installer.p12"
keychain_path="$signing_dir/build.keychain-db"
security create-keychain -p "$PORTWAY_MAC_KEYCHAIN_PASSWORD" "$keychain_path"
security set-keychain-settings -lut 21600 "$keychain_path"
security unlock-keychain -p "$PORTWAY_MAC_KEYCHAIN_PASSWORD" "$keychain_path"
security import "$signing_dir/application.p12" -P "$PORTWAY_MAC_P12_PASSWORD" -A -t cert -f pkcs12 -k "$keychain_path"
security import "$signing_dir/installer.p12" -P "$PORTWAY_MAC_P12_PASSWORD" -A -t cert -f pkcs12 -k "$keychain_path"
security set-key-partition-list -S apple-tool:,apple: -k "$PORTWAY_MAC_KEYCHAIN_PASSWORD" "$keychain_path"
security list-keychains -d user -s "$keychain_path" login.keychain-db
xcrun notarytool store-credentials "$PORTWAY_MAC_NOTARY_PROFILE" --apple-id "$PORTWAY_APPLE_ID" --team-id "$PORTWAY_APPLE_TEAM_ID" --password "$PORTWAY_APPLE_APP_PASSWORD" --keychain "$keychain_path"
printf 'PORTWAY_MAC_KEYCHAIN=%s\n' "$keychain_path" >> "$GITHUB_ENV"
