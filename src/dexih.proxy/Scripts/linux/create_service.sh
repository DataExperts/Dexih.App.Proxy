sudo cp dexih.proxy.service /etc/systemd/system
sudo systemctl daemon-reload
sudo systemctl enable dexih.proxy.service
sudo systemctl start dexih.proxy.service
