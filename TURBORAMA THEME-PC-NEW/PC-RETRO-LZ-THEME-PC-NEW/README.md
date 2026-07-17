cd ~/batocera-43

sed -i \
's/^ES_THEME_LZGAMEBOX_VERSION *=.*/ES_THEME_LZGAMEBOX_VERSION = 7f7bd7af93851431adcc61d53a30fb0f9638149b/' \
$(grep -RIl "^ES_THEME_LZGAMEBOX_VERSION" package | head -n1)

rm -rf output/x86_64/build/es-theme-lzgamebox-*
rm -rf output/x86_64/target/usr/share/emulationstation/themes/LZGameBOX
rm -rf dl/es-theme-lzgamebox
rm -f dl/es-theme-lzgamebox-*

make x86_64-pkg PKG=es-theme-lzgamebox

git -C output/x86_64/build/es-theme-lzgamebox-* rev-parse HEAD


cd ~/Documentos/PC-RETRO-LZ-THEME-PC-NEW

git status
git add .
git commit -m "Atualiza tema e arquivos"
git pull --rebase origin main
git push origin main


nautilus smb://192.168.0.127
