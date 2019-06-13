#!/usr/bin/env bash

sudo apt-get update
sudo apt-get install
cd /etc/nginx/sites-available/

# edit the default
# server {
#     server_name 1-1-1-1.proxy.dexih.com;
#     listen 80;
#     location / {
#         proxy_pass http://localhost:5000;
#     }


sudo systemctl reload nginx

sudo certbot --nginx -d 1-1-1-1.proxy.dexih.com