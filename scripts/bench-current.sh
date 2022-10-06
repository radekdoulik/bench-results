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
    checkout_args=main
    while [ $# -gt 0 ]
    do
	case "$1" in
            -h)
		shift
                echo Build for hash $1
		checkout_args=$1
		shift
		;;
	    -s)
		shift
		echo Build in separate folder
		separate_folder=1
		;;
	    -b)
		shift
		echo Build only, no measurement runs
		build_only=1
		;;
            *)
                echo Build for date $1
                checkout_args=`git rev-list -n 1 --before="$1 23:59:59" main`
                ;;
	esac
    done

    if [ ${separate_folder} -gt 0 ]
    then
	repo_folder=~/git/runtime-${checkout_args}
	if [ ! -d ${repo_folder} ]
	then
	    mkdir -p ${repo_folder}
	fi
	if [ ! -d ${repo_folder}/.git ]
	then
	    echo Copying .git
	    cp -r ~/git/runtime/.git ${repo_folder}/
	fi
    else
	repo_folder=~/git/runtime
    fi

    echo Prepare tree in ${repo_folder}
    cd ${repo_folder}

    echo Clean tree
    rm -rf src/mono/wasm/emsdk
    git clean -xfd
    git stash

    echo Checkout ${checkout_args} and pull -r
    git checkout ${checkout_args}
    git pull -r

    if ! grep results.json src/mono/sample/wasm/browser-bench/main.js
    then
        echo browser-bench too old, using replacement
        mv src/mono/sample/wasm/browser-bench src/mono/sample/wasm/browser-bench-bak
        rm -rf src/mono/sample/wasm/browser-bench
        cp -r ~/git/browser-bench src/mono/sample/wasm/
    fi

    HASH=`git rev-parse HEAD`

    if [ "`cat src/mono/wasm/emscripten-version.txt`" == "3.1.12" ]
    then
	echo Using 3.1.13 emscripten instead of 3.1.12 - which is not available on arm64 linux
	echo -n 3.1.13 > src/mono/wasm/emscripten-version.txt
    fi

    export EMSDK_PATH=${repo_folder}/src/mono/wasm/emsdk
    cd src/mono/wasm
    make provision-wasm
    cd -

    git apply ../runtime.patch

    rm -rf artifacts
}

prepare_environment() {
    HASH=`git rev-parse HEAD`
    echo Hash $HASH
    git log -1

    echo Prepare build of $HASH
    RESULTS_DIR=~/WasmPerformanceMeasurements/measurements/$HASH
    mkdir -p $RESULTS_DIR
    cd $RESULTS_DIR
    uname -a > system.txt
    echo === outpuf of: free > hw-info.txt
    free >> hw-info.txt
    echo === outpuf of: cat /proc/meminfo >> hw-info.txt
    cat /proc/meminfo >> hw-info.txt
    echo === outpuf of: cat /proc/cpuinfo >> hw-info.txt
    cat /proc/cpuinfo >> hw-info.txt
    cp ${repo_folder}/src/mono/wasm/emscripten-version.txt .
    chromium --version 2>&1| tail -1 >> versions.txt
    firefox --version 2>&1| tail -1 >> versions.txt
    cd -

    echo Copy libclang
    mkdir -p artifacts/obj/mono/Browser.wasm.Release/cross/llvm/lib
    cp -v ../llvm-project/artifacts/obj/InstallRoot-arm64/lib/libclang.so* artifacts/obj/mono/Browser.wasm.Release/cross/llvm/lib/

    LOG_HASH_DATE=`git log -1 --pretty="format:%H %ad"`
}

build_runtime() {
    echo Build runtime
    cd ${repo_folder}
    retries=0
    while true; do
	killall dotnet
        ./build.sh -bl -os Browser -subset mono+libs+packs -c Release
	build_exit_code=$?
	[ $build_exit_code -eq 0 ] && break;
        if [ $retries -gt 2 ]; then
            echo Too many retries $retries
	    echo Build exit code: $build_exit_code
            exit 1
        fi
	echo Retrying build
        ((retries++))
    done
}

build_sample() {
    killall chrome
    killall firefox
    killall HttpServer
    sleep 2
    killall -9 chrome
    killall -9 firefox
    killall -9 HttpServer

    echo Build bench sample with additional params: $@
    cd ${repo_folder}
    rm -rf artifacts/obj/mono/Wasm.Browser.Bench.Sample
    rm -rf src/mono/sample/wasm/browser-bench/bin
    echo Cleaned old build
    ls src/mono/sample/wasm/browser-bench/bin
    echo ./dotnet.sh build -c Release /t:BuildSampleInTree $@ src/mono/sample/wasm/browser-bench/Wasm.Browser.Bench.Sample.csproj
    ./dotnet.sh build -c Release /t:BuildSampleInTree $@ src/mono/sample/wasm/browser-bench/Wasm.Browser.Bench.Sample.csproj
}

run_sample_start() {
    echo HttpServer
    ## ./dotnet.sh build -c Release src/mono/sample/wasm/simple-server/

    echo Restart $2
    killall chrome
    killall firefox
    killall HttpServer
    sleep 2
    killall -9 chrome
    killall -9 firefox
    killall -9 HttpServer

    echo Run bench
    cd ${repo_folder}/src/mono/sample/wasm/browser-bench/bin/Release/AppBundle
    rm -f results.*
    echo Cleaned old results
    ls results.*
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
	      sleep 5
              killall HttpServer
	      sleep 2
              killall -9 HttpServer
              break
          fi
	  if [ $wait_time -gt 1800 ]; then
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

echo Called with $@

clean_environment
prepare_tree $@
prepare_environment

build_runtime

build_sample -p:RunAOTCompilation=true
if [ ${build_only} -gt 0 ]
then
    echo Build done
    exit 0
fi

run_sample aot/default/chrome chrome chromium
run_sample aot/default/firefox firefox firefox

build_sample -p:RunAOTCompilation=true -p:BuildAdditionalArgs="-p:WasmSIMD=true%20-p:WasmEnableSIMD=true"
# seems broken on linux/arm64: run_sample aot/simd/chrome chrome chromium
run_sample aot/simd/firefox firefox firefox

build_sample -p:RunAOTCompilation=true -p:BuildAdditionalArgs="-p:WasmExceptionHandling=true%20-p:WasmEnableExceptionHandling=true"
run_sample aot/wasm-eh/chrome chrome chromium
run_sample aot/wasm-eh/firefox firefox firefox

build_sample -p:RunAOTCompilation=true -p:BuildAdditionalArgs="-p:WasmSIMD=true%20-p:WasmEnableSIMD=true%20-p:WasmExceptionHandling=true%20-p:WasmEnableExceptionHandling=true"
# seems broken on linux/arm64: run_sample aot/simd/chrome chrome chromium
run_sample aot/simd+wasm-eh/firefox firefox firefox

build_sample -p:RunAOTCompilation=false
run_sample interp/default/chrome chrome chromium
run_sample interp/default/firefox firefox firefox

cd $RESULTS_DIR/../..
find measurements -name results.json | grep -v AppBundle > measurements/jsonDataFiles.txt
DOTNET_ROOT=~/dotnet ~/bench-results-tools/WasmBenchmarkResults/bin/Release/net6.0/WasmBenchmarkResults
cd $RESULTS_DIR

git add . ../../README.md ../../csv ../jsonDataFiles.txt ../index.zip
echo Adding commit for: $LOG_HASH_DATE
git commit -m "Add results for: $LOG_HASH_DATE"

echo Done
