#!/bin/bash

clean_environment()
{
    killall chrome
    killall firefox
    killall dotnet
    killall HttpServer
    sudo systemctl restart display-manager
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
    mt_only=0
}

fix_emscripten_env() {
    if [ -f src/mono/wasm/emscripten-version.txt ]
    then
        emscripten_version=`cat src/mono/wasm/emscripten-version.txt`
    else
        emscripten_version=`cat src/mono/browser/emscripten-version.txt`
    fi
    if [ "${emscripten_version}" == "3.1.30" ]
    then
        echo Using local 3.1.30 emscripten instead of prebuilt
        export EMSDK_PATH=/home/rodo/git/emsdk-3130
        export LD_LIBRARY_PATH=/home/rodo/git/binaryen/lib
        export PATH=/home/rodo/git/emscripten:$PATH
        emscripten_provisioned=1
    fi
    if [ "${emscripten_version}" == "3.1.34" ]
    then
        echo Using local 3.1.34 emscripten instead of prebuilt
        export EMSDK_PATH=/home/rodo/git/emsdk-3134
        export LD_LIBRARY_PATH=/home/rodo/git/binaryen-3134/lib
        export PATH=/home/rodo/git/emscripten-3134:$PATH
        emscripten_provisioned=1
    fi
    echo Emscripten version: ${emscripten_version}
    echo Emscripten    path: ${EMSDK_PATH}
}

git_pull() {
    git pull -r
    if [ $? != 0 ]
    then
        git rebase --skip
        git pull -r
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
	    -t)
		shift
		echo Multithreaded only
		mt_only=1
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
    git stash
    git checkout ${checkout_args}
    git_pull

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
        ./build.sh -bl -os Browser -subset mono+libs+packs -c Release $1
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

    if [ ${mt_only} -lt 1 ]; then
        echo Build WBT
        ./dotnet.sh build src/mono/wasm/Wasm.Build.Tests/Wasm.Build.Tests.csproj -bl -c Release -t:Test -p:TargetOS=browser -p:TargetArchitecture=wasm -p:XUnitClassName=none $1 $2

        echo Prepare blazor-frame build
        cd ${repo_folder}/src/mono/sample/wasm/blazor-frame
        cp -v ../../../../../src/mono/wasm/Wasm.Build.Tests/data/WasmOverridePacks.targets .
        cp -v ../../../../../src/mono/wasm/Wasm.Build.Tests/data/Blazor.Directory.Build.targets Directory.Build.targets
        # prepare nuget config
        echo "<?xml version=\"1.0\" encoding=\"utf-8\"?>
<configuration>
  <!-- Don't use any higher level config files. -->
  <fallbackPackageFolders>
    <clear />
  </fallbackPackageFolders>
  <packageSources>
    <clear />
    <add key=\"nuget-local\" value=\"${repo_folder}/artifacts/packages/Release/Shipping/\" />
    <add key=\"dotnet8\" value=\"https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet8/nuget/v3/index.json\" />
    <add key=\"nuget.org\"  value=\"https://api.nuget.org/v3/index.json\" protocolVersion=\"3\" />
  </packageSources>
    <disabledPackageSources>
    <clear />
  </disabledPackageSources>
</configuration>
" > nuget.config

        echo nuget.config:
        cat nuget.config

        echo Prepare browser-frame build
        cd ${repo_folder}/src/mono/sample/wasm/browser-frame
        cp -v ../../../../../src/mono/wasm/Wasm.Build.Tests/data/WasmOverridePacks.targets .
        cp -v ../../../../../src/mono/wasm/Wasm.Build.Tests/data/Blazor.Directory.Build.targets Directory.Build.targets
        # prepare nuget config
        echo "<?xml version=\"1.0\" encoding=\"utf-8\"?>
    <configuration>
      <!-- Don't use any higher level config files. -->
      <fallbackPackageFolders>
        <clear />
      </fallbackPackageFolders>
      <packageSources>
        <clear />
        <add key=\"nuget-local\" value=\"${repo_folder}/artifacts/packages/Release/Shipping/\" />
        <add key=\"dotnet8\" value=\"https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet8/nuget/v3/index.json\" />
        <add key=\"nuget.org\"  value=\"https://api.nuget.org/v3/index.json\" protocolVersion=\"3\" />
      </packageSources>
        <disabledPackageSources>
        <clear />
      </disabledPackageSources>
    </configuration>
    " > nuget.config

        echo nuget.config:
        cat nuget.config

        echo Prepare dotnet-latest environment
        export DOTNET_ROOT=${repo_folder}/artifacts/bin/dotnet-latest
        export PATH="${DOTNET_ROOT}:${PATH}"
    fi

    cd ${repo_folder}

    echo Runtime build done
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

    echo Fix library path
    old_LD_LIBRARY_PATH=${LD_LIBRARY_PATH}
    unset LD_LIBRARY_PATH
    echo LD_LIBRARY_PATH=${LD_LIBRARY_PATH}

    ls src/mono/sample/wasm/browser-bench/bin
    build_cmd="./dotnet.sh build -c Release /t:BuildSampleInTree -p:WasmMemorySnapshotNodeExecutable=\"`which node`\" $@ src/mono/sample/wasm/browser-bench/Wasm.Browser.Bench.Sample.csproj"
    echo ${build_cmd}
    #./dotnet.sh build -c Release /t:BuildSampleInTree $@ src/mono/sample/wasm/browser-bench/Wasm.Browser.Bench.Sample.csproj
    ${build_cmd}

    echo Restored library path
    export LD_LIBRARY_PATH=${old_LD_LIBRARY_PATH}
    echo LD_LIBRARY_PATH=${LD_LIBRARY_PATH}

    # if [ ${mt_only} -lt 1 ]
    # then
	# echo Build the blazor-frame

    #     export DOTNET_ROOT=${repo_folder}/artifacts/bin/dotnet-latest
    #     export PATH="${DOTNET_ROOT}:${PATH}"

    #     cd ${repo_folder}/src/mono/sample/wasm/blazor-frame
    #     rm -rf bin obj
    #     old_LD_LIBRARY_PATH=${LD_LIBRARY_PATH}
    #     unset LD_LIBRARY_PATH
    #     echo LD_LIBRARY_PATH=${LD_LIBRARY_PATH}
    #     echo dotnet publish -c Release -p:WBTOverrideRuntimePack=true $@
    #     dotnet publish -c Release -p:WBTOverrideRuntimePack=true $@
    #     export LD_LIBRARY_PATH=${old_LD_LIBRARY_PATH}
    #     echo LD_LIBRARY_PATH=${LD_LIBRARY_PATH}

    #     echo Link blazor-frame
    #     cd ${repo_folder}/src/mono/sample/wasm/browser-bench/bin/Release/AppBundle
    #     ln -s ${repo_folder}/src/mono/sample/wasm/blazor-frame/bin/Release/net8.0/publish/wwwroot/blazor-template .
    #     echo ls ${repo_folder}/src/mono/sample/wasm/browser-bench/bin/Release/AppBundle/blazor-template
    #     ls ${repo_folder}/src/mono/sample/wasm/browser-bench/bin/Release/AppBundle/blazor-template
    #     cd ${repo_folder}

    #     echo Build the browser-frame

    #     cd ${repo_folder}/src/mono/sample/wasm/browser-frame
    #     rm -rf bin obj
    #     old_LD_LIBRARY_PATH=${LD_LIBRARY_PATH}
    #     unset LD_LIBRARY_PATH
    #     echo LD_LIBRARY_PATH=${LD_LIBRARY_PATH}
    #     echo dotnet publish -c Release -p:WBTOverrideRuntimePack=true $@
    #     dotnet publish -c Release -p:WBTOverrideRuntimePack=true -p:PublishTrimmed=true $@
    #     export LD_LIBRARY_PATH=${old_LD_LIBRARY_PATH}
    #     echo LD_LIBRARY_PATH=${LD_LIBRARY_PATH}

    #     echo Link browser-frame
    #     cd ${repo_folder}/src/mono/sample/wasm/browser-bench/bin/Release/AppBundle
    #     ln -s ${repo_folder}/src/mono/sample/wasm/browser-frame/bin/Release/net8.0/publish/wwwroot ./browser-template
    #     echo ls ${repo_folder}/src/mono/sample/wasm/browser-bench/bin/Release/AppBundle/browser-template
    #     ls ${repo_folder}/src/mono/sample/wasm/browser-bench/bin/Release/AppBundle/browser-template
    #     cd ${repo_folder}

    #     FLAVOR_RESULTS_DIR=${RESULTS_DIR}/${sample_flavor_dir}
    #     APPBUNDLE_COPY=${FLAVOR_RESULTS_DIR}/AppBundle
    #     echo Copy ${repo_folder}/src/mono/sample/wasm/browser-bench/bin/Release/AppBundle to ${APPBUNDLE_COPY}
    #     mkdir -p ${FLAVOR_RESULTS_DIR}
    #     cp -lrv ${repo_folder}/src/mono/sample/wasm/browser-bench/bin/Release/AppBundle ${FLAVOR_RESULTS_DIR}/
    #     du -sh ${APPBUNDLE_COPY}
    # fi

    FLAVOR_RESULTS_DIR=${RESULTS_DIR}/${sample_flavor_dir}
    APPBUNDLE_COPY=${FLAVOR_RESULTS_DIR}/AppBundle
    echo Copy ${repo_folder}/src/mono/sample/wasm/browser-bench/bin/Release/AppBundle to ${APPBUNDLE_COPY}
    mkdir -p ${FLAVOR_RESULTS_DIR}
    cp -lrv ${repo_folder}/src/mono/sample/wasm/browser-bench/bin/Release/AppBundle ${FLAVOR_RESULTS_DIR}/
    du -sh ${APPBUNDLE_COPY}

    echo Build bench sample done
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
    export DOTNET_ROOT=${repo_folder}/.dotnet/
    echo Start http server in `pwd`
    rm -f server.log
    server_wait_time=0
    if [ ${mt_only} -gt 0 ]
    then
        uexclusions="?exclusions=AppStart:.*cold,JSInterop:.*Task,WebSocket"
        texclusions=-s "$uexclusions"
    fi

    # ~/simple-server/bin/Release/net8.0/HttpServer > server.log &
    ${repo_folder}/src/mono/sample/wasm/simple-server/bin/Release/net8.0/HttpServer $texclusions > server.log &

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
    complete_url=$BENCH_URL$4${url_suffix}${uexclusions}
    echo Url: $complete_url
    if [ "$3" == "firefox" ]; then
        private_arg="--private-window"
    else
        private_arg="--incognito"
    fi
    echo Start $3 $private_arg ${complete_url} &
    DISPLAY=:0 $3 $private_arg ${complete_url} &
}

run_sample() {
    if [ "$3" != "firefox" ] && [ $firefox_only -gt 0 ]; then
	echo Skip $3
	return;
    fi
    sudo systemctl restart display-manager
    sleep 3
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
	  if [ $wait_time -gt 2400 ]; then
	      echo "Timeout after $wait_time ($retries retries)"
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
    cat $FLAVOR_RESULTS_DIR/git-log.txt

    echo Run finished - $1:$2:$3
}

echo Called with $@

clean_environment
prepare_tree $@
prepare_environment

(cd ~/WasmPerformanceMeasurements/; git stash; git_pull)
echo Check hash: "$HASH" == "`cat ~/WasmPerformanceMeasurements/latest.txt`"
if "$HASH" == "`cat ~/WasmPerformanceMeasurements/latest.txt`"
then
    echo $HASH is already latest measurement => exit
    exit 0
fi

startup_props="-p:BlazorStartup=true -p:BrowserStartup=true"
snapshot_node="-p:WasmMemorySnapshotNodeExecutable=\"`which node`\""

if [ ${mt_only} -lt 1 ]
then
    build_runtime

    sample_flavor_dir=aot/default
    build_sample -p:RunAOTCompilation=true ${startup_props} -p:BuildAdditionalArgs="${snapshot_node}%20-p:RunAOTCompilation=true"
    if [ ${build_only} -gt 0 ]
    then
        echo Build done
        exit 0
    fi

    run_sample ${sample_flavor_dir}/chrome chrome chromium
    run_sample ${sample_flavor_dir}/firefox firefox firefox

    if [ ! ${default_flavor_only} -gt 0 ]
    then
    sample_flavor_dir=aot/legacy
	build_sample -p:RunAOTCompilation=true ${startup_props} -p:BuildAdditionalArgs="-p:RunAOTCompilation=true%20-p:WasmSIMD=false%20-p:WasmEnableSIMD=false%20${snapshot_node}%20-p:WasmExceptionHandling=false%20-p:WasmEnableExceptionHandling=false%20"
	run_sample ${sample_flavor_dir}/chrome chrome chromium
	run_sample ${sample_flavor_dir}/firefox firefox firefox

    #   sample_flavor_dir=aot/wasm-eh
    # 	build_sample -p:RunAOTCompilation=true ${startup_props} -p:BuildAdditionalArgs="-p:RunAOTCompilation=true%20-p:WasmExceptionHandling=true%20-p:WasmEnableExceptionHandling=true%20${snapshot_node}"
    # 	run_sample aot/wasm-eh/chrome chrome chromium
    # 	run_sample aot/wasm-eh/firefox firefox firefox

        sample_flavor_dir=aot/hybrid-globalization
        build_sample -p:RunAOTCompilation=true ${startup_props} -p:BuildAdditionalArgs="-p:RunAOTCompilation=true%20-p:HybridGlobalization=true"
        run_sample ${sample_flavor_dir}/chrome chrome chromium "?task=String"
    #	run_sample aot/hybrid-globalization/firefox firefox firefox "?task=String"   firefox is missing Intl.segmenter

        sample_flavor_dir=interp/hybrid-globalization
        build_sample ${startup_props} -p:BuildAdditionalArgs="$-p:HybridGlobalization=true"
        run_sample ${sample_flavor_dir}/chrome chrome chromium "?task=String"

        #	build_sample -p:RunAOTCompilation=true ${startup_props} -p:BuildAdditionalArgs="-p:WasmSIMD=true%20-p:WasmEnableSIMD=true%20-p:WasmExceptionHandling=true%20-p:WasmEnableExceptionHandling=true%20${snapshot_node}"
        #	run_sample aot/simd+wasm-eh/chrome chrome chromium
        #	run_sample aot/simd+wasm-eh/firefox firefox firefox
    fi

    sample_flavor_dir=interp/default
    build_sample -p:RunAOTCompilation=false ${startup_props} -p:BuildAdditionalArgs=""
    run_sample ${sample_flavor_dir}/chrome chrome chromium
    run_sample ${sample_flavor_dir}/firefox firefox firefox
else # MT
    build_runtime -p:MonoWasmBuildVariant=multithread -p:WasmEnableThreads=true

#    build_sample -p:RunAOTCompilation=true -p:BuildAdditionalArgs="-p:RunAOTCompilation=true%20${snapshot_node}%20-p:WasmEnableThreads=true"
    if [ ${build_only} -gt 0 ]
    then
	echo Build done
	exit 0
    fi

    sample_flavor_dir=interp/threads
    build_sample -p:RunAOTCompilation=false -p:BuildAdditionalArgs="-p:WasmEnableThreads=true"
    run_sample ${sample_flavor_dir}/chrome chrome chromium
    #run_sample ${sample_flavor_dir}/firefox firefox firefox
fi #MT

cd $RESULTS_DIR/../..
#find measurements -name results.json | grep -v AppBundle > measurements/jsonDataFiles.txt
git_pull

echo DOTNET_ROOT=~/dotnet ~/bench-results-tools/WasmBenchmarkResults/bin/Release/net6.0/WasmBenchmarkResults -v -a measurements/$HASH -i measurements/index2.zip
DOTNET_ROOT=~/dotnet ~/bench-results-tools/WasmBenchmarkResults/bin/Release/net6.0/WasmBenchmarkResults -v -a measurements/$HASH -i measurements/index2.zip
#mv measurements/index.zip measurements/index2.zip
#DOTNET_ROOT=~/dotnet ~/bench-results-tools-old/WasmBenchmarkResults/bin/Release/net6.0/WasmBenchmarkResults
cd $RESULTS_DIR

if [ "${dont_commit}" -eq 0 ]
then
	echo Adding `pwd` to commit, should be $RESULTS_DIR
	echo -n $HASH > ../latest.txt
	git add . ../../README.md ../../csv ../jsonDataFiles.txt ../index2.zip ../latest.txt ../slices/
	echo Adding commit for: $LOG_HASH_DATE
	git commit -m "Add results for: $LOG_HASH_DATE"
	echo Push to repo
	git push
	push_result=$?
	echo Result: ${push_result}
	if [ "${push_result}" -gt 0 ]
	then
		echo Recovering from failed push of $HASH
		cd $RESULTS_DIR/../..
		failed_dir="failed_pushes"
		mkdir -p ${failed_dir}
		mv measurements/$HASH ${failed_dir}/
		git reset HEAD~1
		git checkout measurements/index2.zip measurements/latest.txt measurements/slices
		git stash
		git pull -r
		echo Recovery pull result $?
	fi
fi

clean_environment

echo Done
date
