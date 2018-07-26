#!/usr/bin/env bash

APPNAME=$1
GROUP=$2
GIT="https://garyholland@${APPNAME}.scm.azurewebsites.net:443/${APPNAME}.git"

DEPLOY="~/Source/Deploy/${APPNAME}"

mkdir ~/Source/Deploy/${APPNAME}

pushd ~/Source/Deploy/${APPNAME}
DEPLOYDIR=$(pwd)
popd

echo $GIT
echo $DEPLOYDIR

# Create the resource group
az group create --name $GROUP --location EastUS

# Create an App Service plan.
az appservice plan create --name $APPNAME --resource-group $GROUP --sku FREE

# Create the Web App
az webapp create --name $APPNAME --resource-group $GROUP --plan $APPNAME
az webapp deployment source config-local-git -n $APPNAME -g $GROUP --query [url] -o tsv

# Configure the app settings
az webapp config appsettings set -g $GROUP -n $APPNAME --settings AppSettings:GoogleClientId=$GoogleClientId AppSettings:MicrosoftClientId=$MicrosoftClientId AppSettings:MicrosoftClientSecret=$MicrosoftClientSecret AppSettings:EncryptionKey=$EncryptionKey AppSettings:GoogleClientSecret=$GoogleClientSecret AppSettings:SendGrid_API=$SendGrid_API AppSettings:RepositoryType=$RepositoryType
az webapp config connection-string set -g $GROUP -n $APPNAME -t $RepositoryType --settings  DefaultConnection='${DefaultConnection}'

mkdir $DEPLOYDIR

pushd ~Source/Dexih.App.Proxy/src/dexih.proxy
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

pushd $DEPLOYDIR
git init
git add --all
git commit -a -m "update"

git remote add azure $GIT
git push azure master
popd
