### Getting local dev certs
dotnet dev-certs https --format pem -ep development.pem -np
and place them in the environment/development/ssl_certs folder

### How to startup
docker build . -t aghanims-wager-spa
podman run --name aghanims-wager-spa --mount type=bind,source=/etc/letsencrypt,destination=/etc/letsencrypt --mount type=bind,source=/var/lib/letsencrypt,destination=/var/lib/letsencrypt -d -p 80:80 -p 443:443 aghanims-wager-spa

### Testing locally with npm run serve
You can navigate to the vue-app folder of the microfrontend and run it using `npm run serve:standalone`. If you run a `docker-compose up` from the development environment prior to this you can point the devServer for the serve standalone to localhost:5001 in the vue.config.js file. This makes it easier to iterate on frontend tests against new controller methods without having to rebuild the entire container image each time.