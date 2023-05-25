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

    build_only=0
    firefox_only=0
    measure_only=0
    default_flavor_only=0
    separate_folder=0
    dont_commit=0
    emscripten_provisioned=0
}

fix_emscripten_env() {
    if [ "`cat src/mono/wasm/emscripten-version.txt`" == "3.1.30" ]
    then
	echo Using local 3.1.30 emscripten instead of prebuilt
	export EMSDK_PATH=/home/rodo/git/emsdk-3130
	export LD_LIBRARY_PATH=/home/rodo/git/binaryen/lib
	export PATH=/home/rodo/git/emscripten:$PATH
	emscripten_provisioned=1
    fi
    if [ "`cat src/mono/wasm/emscripten-version.txt`" == "3.1.34" ]
    then
	echo Using local 3.1.34 emscripten instead of prebuilt
	export EMSDK_PATH=/home/rodo/git/emsdk-3134
	export LD_LIBRARY_PATH=/home/rodo/git/binaryen-3134/lib
	export PATH=/home/rodo/git/emscripten-3134:$PATH
	emscripten_provisioned=1
    fi
}

prepare_tree() {
    do_fetch=0
    checkout_args=main
    while [ $# -gt 0 ]
    do
	case "$1" in
            -h)
		shift
                echo Build for hash $1
		do_fetch=1
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
	    --dont-commit)
		shift
		echo Do not commit results
		dont_commit=1
		;;
	    -f)
		shift
		echo Firefox only
		firefox_only=1
		;;
	    -m)
		shift
		echo Measure only, skip runtime build
		measure_only=1
		;;
	    -d)
		shift
		echo Default flavors only, skip other flavors
		default_flavor_only=1
		;;
	    -a)
		shift
		echo Additional URL suffix $1
		url_suffix=$1
		shift
		;;
            *)
                echo Build for date $1
		cd ~/git/runtime
		git fetch origin
                checkout_args=`git rev-list -n 1 --before="$1 23:59:59" origin/main`
		shift
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

    cd ${repo_folder}

    if [ ${do_fetch} -gt 0 ]
    then
	git fetch --all
    fi

    if [ ${measure_only} -gt 0 ]
    then
	fix_emscripten_env
	return
    fi

    echo Prepare tree in ${repo_folder}

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

#    if [ "`cat src/mono/wasm/emscripten-version.txt`" == "3.1.30" ]
#    then
    fix_emscripten_env
    if [ ${emscripten_provisioned} -lt 1 ]
    then
        cd src/mono/wasm
        make provision-wasm
        cd -
    fi

    git apply ../runtime.patch
    git apply ../runtime.2.patch

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

    if [ ! ${measure_only} -gt 0 ]
    then
	echo Copy libclang
	mkdir -p artifacts/obj/mono/Browser.wasm.Release/cross/llvm/lib
	cp -v ../llvm-project/artifacts/obj/InstallRoot-arm64/lib/libclang.so* artifacts/obj/mono/Browser.wasm.Release/cross/llvm/lib/
    fi

    LOG_HASH_DATE=`git log -1 --pretty="format:%H %ad"`
}

build_runtime() {
    if [ ${measure_only} -gt 0 ]
    then
	return
    fi

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
    echo Build HttpServer
    ./dotnet.sh build -c Release src/mono/sample/wasm/simple-server/HttpServer.csproj
    Runtime build done
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
    build_cmd="./dotnet.sh build -c Release /t:BuildSampleInTree -p:WasmMemorySnapshotNodeExecutable=\"`which node`\" $@ src/mono/sample/wasm/browser-bench/Wasm.Browser.Bench.Sample.csproj"
    echo ${build_cmd}
    #./dotnet.sh build -c Release /t:BuildSampleInTree $@ src/mono/sample/wasm/browser-bench/Wasm.Browser.Bench.Sample.csproj
    ${build_cmd}
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
    rm -f results.* ../../../results.*
    echo Cleaned old results
    ls results.* ../../../results.*
    export DOTNET_ROOT=~/dotnet/
    echo Start http server in `pwd`
    rm -f server.log
    server_wait_time=0
    # ~/simple-server/bin/Release/net6.0/HttpServer > server.log &
    ${repo_folder}/src/mono/sample/wasm/simple-server/bin/Release/net6.0/HttpServer > server.log &

    until [ -f server.log ]
    do
        sleep 1
        ((server_wait_time += 1))
        if [ $server_wait_time -gt 30 ]; then
            echo Unable to start server
            return
        fi
    done
    BENCH_URL=`head -1 server.log | sed -e 's/Listening on //'`
    echo Url: $BENCH_URL$4${url_suffix}
    if [ "$3" == "firefox" ]; then
        private_arg="--private-window"
    else
        private_arg="--incognito"
    fi
    echo Start $3 $private_arg $BENCH_URL$4${url_suffix} &
    DISPLAY=:0 $3 $private_arg $BENCH_URL$4${url_suffix} &
}

run_sample() {
    if [ "$3" != "firefox" ] && [ $firefox_only -gt 0 ]; then
	echo Skip $3
	return;
    fi
    rm -f bootstrap.flag
    run_sample_start $@
    retries=0
    bootstrap_retries=0
    echo Wait for bench to finish
    sleep 5
    wait_time=5
    echo Waked
    while true; do
          sleep 5
          ((wait_time += 5))
          if [ ! -f bootstrap.flag ]; then
                if [ $bootstrap_retries -gt 6 ]; then
                    echo Too many retries $bootstrap_retries
                    break
                fi
                ((bootstrap_retries++))
                echo "Bootstrap failed, retrying (retries: $bootstrap_retries)"
                run_sample_start $@
                sleep 5
                wait_time=5
                continue
          fi
          if [ -f results.json ]; then
	      echo Finished after $wait_time seconds, retries: $retries, bootstraps: $bootstrap_retries
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
          bootstrap_retries=0
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

(cd ~/WasmPerformanceMeasurements/; git pull -r)

if "$HASH" == "`cat ~/WasmPerformanceMeasurements/latest.txt`"
then
    echo $HASH is already latest measurement => exit
    exit 0
fi

build_runtime

snapshot_node="-p:WasmMemorySnapshotNodeExecutable=\"`which node`\""

build_sample -p:RunAOTCompilation=true -p:BuildAdditionalArgs="${snapshot_node}"
if [ ${build_only} -gt 0 ]
then
    echo Build done
    exit 0
fi

run_sample aot/default/chrome chrome chromium
run_sample aot/default/firefox firefox firefox

if [ ! ${default_flavor_only} -gt 0 ]
then
	build_sample -p:RunAOTCompilation=true -p:BuildAdditionalArgs="-p:WasmSIMD=false%20-p:WasmEnableSIMD=false%20${snapshot_node} -p:WasmExceptionHandling=false%20-p:WasmEnableExceptionHandling=false%20"
	run_sample aot/legacy/chrome chrome chromium
	run_sample aot/legacy/firefox firefox firefox

# 	build_sample -p:RunAOTCompilation=true -p:BuildAdditionalArgs="-p:WasmExceptionHandling=true%20-p:WasmEnableExceptionHandling=true%20${snapshot_node}"
# 	run_sample aot/wasm-eh/chrome chrome chromium
# 	run_sample aot/wasm-eh/firefox firefox firefox

	build_sample -p:RunAOTCompilation=true -p:BuildAdditionalArgs="-p:HybridGlobalization=true"
	run_sample aot/hybrid-globalization/chrome chrome chromium "?task=String"
#	run_sample aot/hybrid-globalization/firefox firefox firefox "?task=String"   firefox is missing Intl.segmenter

	build_sample -p:BuildAdditionalArgs="-p:HybridGlobalization=true"
	run_sample interp/hybrid-globalization/chrome chrome chromium "?task=String"

#	build_sample -p:RunAOTCompilation=true -p:BuildAdditionalArgs="-p:WasmSIMD=true%20-p:WasmEnableSIMD=true%20-p:WasmExceptionHandling=true%20-p:WasmEnableExceptionHandling=true%20${snapshot_node}"
#	run_sample aot/simd+wasm-eh/chrome chrome chromium
#	run_sample aot/simd+wasm-eh/firefox firefox firefox
fi

build_sample -p:RunAOTCompilation=false
run_sample interp/default/chrome chrome chromium
run_sample interp/default/firefox firefox firefox

cd $RESULTS_DIR/../..
#find measurements -name results.json | grep -v AppBundle > measurements/jsonDataFiles.txt
git pull -r
echo DOTNET_ROOT=~/dotnet ~/bench-results-tools/WasmBenchmarkResults/bin/Release/net6.0/WasmBenchmarkResults -a measurements/$HASH -i measurements/index2.zip
DOTNET_ROOT=~/dotnet ~/bench-results-tools/WasmBenchmarkResults/bin/Release/net6.0/WasmBenchmarkResults -a measurements/$HASH -i measurements/index2.zip
#mv measurements/index.zip measurements/index2.zip
#DOTNET_ROOT=~/dotnet ~/bench-results-tools-old/WasmBenchmarkResults/bin/Release/net6.0/WasmBenchmarkResults
cd $RESULTS_DIR

if [ "${dont_commit}" -eq 0 ]
then
	echo Adding `pwd` to commit, should be $RESULTS_DIR
	echo -n $HASH > ../latest.txt
	git add . ../../README.md ../../csv ../jsonDataFiles.txt ../index2.zip ../latest.txt
	echo Adding commit for: $LOG_HASH_DATE
	git commit -m "Add results for: $LOG_HASH_DATE"
	git push
fi

clean_environment

echo Done
date
