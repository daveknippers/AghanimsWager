### Getting local dev certs
dotnet dev-certs https --format pem -ep development.pem -np
and place them in the environment/development/ssl_certs folder

### How to startup
docker build . -t aghanims-wager-spa
podman run --name aghanims-wager-spa --mount type=bind,source=/etc/letsencrypt,destination=/etc/letsencrypt --mount type=bind,source=/var/lib/letsencrypt,destination=/var/lib/letsencrypt -d -p 80:80 -p 443:443 aghanims-wager-spa