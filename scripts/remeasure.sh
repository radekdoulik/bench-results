killall HttpServer
killall firefox
killall chrome
sleep 2
killall -9 HttpServer
killall -9 firefox
killall -9 chrome

DOTNET_ROOT=~/dotnet ~/simple-server/bin/Release/net6.0/HttpServer > server.log &
sleep 5
BENCH_URL=`sed -e 's/Listening on //' < server.log`
echo Url: $BENCH_URL${url_suffix}
if [[ "`pwd`" == *"firefox"* ]]
then
   BROWSER=firefox
else
   BROWSER=chromium
fi

rm results.json results.html
echo Run ${BROWSER}
DISPLAY=:0 ${BROWSER} ${BENCH_URL}
