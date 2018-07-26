#!/usr/bin/env bash
APPNAME=$1
DEPLOY="~/Source/Deploy/${APPNAME}"

pushd ~/Source/Deploy/${APPNAME}
DEPLOYDIR=$(pwd)
popd

echo "Deploy Dir:" $DEPLOYDIR

echo Building the dexih.api
pushd ~/Source/Dexih.App.Proxy/src/dexih.proxy
    # Get the build version
    VERSION_SUFFIX=`more version.txt`
    
    # Add 1 to the build version, and write back to version.txt
    VERSION_SUFFIX=`printf "%05d\n" $((10#$VERSION_SUFFIX +1))`
    echo 'Building version: '$VERSION_SUFFIX
    printf "%s" ${VERSION_SUFFIX} > version.txt
    
    # publishes with dotnet runtime already installed.
    dotnet publish --version-suffix C${VERSION_SUFFIX} -o ${DEPLOYDIR}

    # publishes standalone
    # dotnet publish -c release -r win7-x64 -f netcoreapp2.1 --version-suffix C${VERSION_SUFFIX} -o ${DEPLOYDIR}
popd

pushd ${DEPLOYDIR}
    git add --all
    git commit -a -m "update"
    git push azure master
popd
