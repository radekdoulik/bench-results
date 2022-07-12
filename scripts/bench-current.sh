#!/bin/bash

clean_environment()
{
    killall chrome
    killall firefox
    killall dotnet
    killall HttpServer
    sleep 2
    killall -9 chrome
    killall -9 firefox
    killall -9 dotnet
    killall -9 HttpServer
}

prepare_tree() {
    rm -rf src/mono/wasm/emsdk
    git clean -xfd
    git stash

    if [ "`cat src/mono/wasm/emscripten-version.txt`" == "3.1.12" ]
    then
	echo Using 3.1.13 emscripten instead of 3.1.12 - which is not available on arm64 linux
	echo -n 3.1.13 > src/mono/wasm/emscripten-version.txt
    fi

    cd src/mono/wasm
    make provision-wasm
    cd -

    if [ $# -gt 0 ]
    then
	echo Build for date $1
        rm -r src/mono/sample/wasm/browser-bench
        git checkout main
        git checkout src/mono/sample/wasm/browser-bench
        git pull -r
        git checkout `git rev-list -n 1 --before="$1 23:59:59" main`
        if ! grep results.json src/mono/sample/wasm/browser-bench/main.js
        then
            mv src/mono/sample/wasm/browser-bench src/mono/sample/wasm/browser-bench-bak
            rm -rf src/mono/sample/wasm/browser-bench
            cp -r ~/git/browser-bench src/mono/sample/wasm/
        fi
        HASH=`git rev-parse HEAD`
    else
        git pull -r
        HASH=`git rev-parse HEAD~1`
    fi

    rm -rf artifacts
}

prepare_environment() {
    HASH=`git rev-parse HEAD`
    echo Hash $HASH
    git log -1

    echo Prepare build of $HASH
    RESULTS_DIR=~/WasmPerformanceMeasurements/measurements/$HASH
    mkdir -p $RESULTS_DIR

    echo Copy libclang
    mkdir -p artifacts/obj/mono/Browser.wasm.Release/cross/llvm/lib
    cp -v ../llvm-project/artifacts/obj/InstallRoot-arm64/lib/libclang.so* artifacts/obj/mono/Browser.wasm.Release/cross/llvm/lib/

    LOG_HASH_DATE=`git log -1 --pretty="format:%H %ad"`
}

build_runtime() {
    echo Build runtime
    cd ~/git/runtime
    ./build.sh -bl -os Browser -subset mono+libs+packs -c Release
}

build_sample_aot() {
    echo Build bench sample with AOT
    cd ~/git/runtime
    rm -rf artifacts/obj/mono/Wasm.Browser.Bench.Sample
    rm -rf src/mono/sample/wasm/browser-bench/bin
    ./dotnet.sh build -c Release /t:BuildSampleInTree -p:RunAOTCompilation=true src/mono/sample/wasm/browser-bench/Wasm.Browser.Bench.Sample.csproj
}

build_sample_interp() {
    echo Build bench sample for interp
    cd ~/git/runtime
    rm -rf artifacts/obj/mono/Wasm.Browser.Bench.Sample
    rm -rf src/mono/sample/wasm/browser-bench/bin
    ./dotnet.sh build -c Release /t:BuildSampleInTree -p:RunAOTCompilation=false src/mono/sample/wasm/browser-bench/Wasm.Browser.Bench.Sample.csproj
}

run_sample_start() {
    echo HttpServer
    ## ./dotnet.sh build -c Release src/mono/sample/wasm/simple-server/

    echo Restart $2
    killall chrome
    killall firefox
    killall HttpServer
    sleep 2

    echo Run bench
    cd ~/git/runtime/src/mono/sample/wasm/browser-bench/bin/Release/AppBundle
    rm results.*
    export DOTNET_ROOT=~/dotnet/
    echo Start http server
    ~/simple-server/bin/Release/net6.0/HttpServer > server.log &
    sleep 3
    BENCH_URL=`sed -e 's/Listening on //' < server.log`
    echo Url: $BENCH_URL
    echo Start $3
    DISPLAY=:0 $3 $BENCH_URL &
}

run_sample() {
    run_sample_start $@
    wait_time=0
    retries=0
    echo Wait for bench to finish
    while true; do
          sleep 5
	  ((wait_time += 5))
          if [ -f results.json ]; then
	      echo Finished after $wait_time seconds
              killall HttpServer
              break
          fi
	  if [ $wait_time -gt 1200 ]; then
	      if [ $retries -gt 2 ]; then
		  echo Too many retries $retries
		  break
	      fi
	      ((retries++))
	      run_sample_start $@
	      wait_time=0
	  fi
    done

    FLAVOR_RESULTS_DIR=$RESULTS_DIR/$1
    mkdir -p $FLAVOR_RESULTS_DIR

    echo Copy results
    cp -v results.* $FLAVOR_RESULTS_DIR
    git log -1 $HASH > $FLAVOR_RESULTS_DIR/git-log.txt
    cp -r . $FLAVOR_RESULTS_DIR/AppBundle
    cat $FLAVOR_RESULTS_DIR/git-log.txt

    echo Run finished - $1:$2:$3
}

cd ~/git/runtime

clean_environment
prepare_tree $@
prepare_environment

build_runtime

build_sample_aot
run_sample aot/default/chrome chrome chromium
run_sample aot/default/firefox firefox firefox

build_sample_interp
run_sample interp/default/chrome chrome chromium
run_sample interp/default/firefox firefox firefox

cd $RESULTS_DIR/../..
find measurements -name results.json | grep -v AppBundle > measurements/jsonDataFiles.txt
DOTNET_ROOT=~/dotnet ~/bench-results-tools/WasmBenchmarkResults/bin/Release/net6.0/WasmBenchmarkResults
cd $RESULTS_DIR
git add . ../../README.md ../../csv ../jsonDataFiles.txt
echo Adding commit for: $LOG_HASH_DATE
git commit -m "Add results for: $LOG_HASH_DATE"

echo Done
