# Aghanims Wager (aghanims-wager-web-app)

Dota Salt Bets

## Install the dependencies
```bash
yarn
# or
npm install
```

### Start the app in development mode (hot-code reloading, error reporting, etc.)
```bash
quasar dev
```


### Lint the files
```bash
yarn lint
# or
npm run lint
```


### Format the files
```bash
yarn format
# or
npm run format
```



### Build the app for production
```bash
quasar build
```


### Customize the configuration
See [Configuring quasar.config.js](https://v2.quasar.dev/quasar-cli-vite/quasar-config-js).

### Getting local dev certs
dotnet dev-certs https --format pem -ep development.pem -np
and place them in the environment/development/ssl_certs folder

### How to startup
docker build . -t aghanims-wager-spa
podman run --name aghanims-wager-spa --mount type=bind,source=/etc/letsencrypt,destination=/etc/letsencrypt --mount type=bind,source=/var/lib/letsencrypt,destination=/var/lib/letsencrypt -d -p 80:80 -p 443:443 aghanims-wager-spa

### Testing locally with npm run serve
You can navigate to the vue-app folder of the microfrontend and run it using `npm run serve:standalone`. If you run a `docker-compose up` from the development environment prior to this you can point the devServer for the serve standalone to localhost:5001 in the vue.config.js file. This makes it easier to iterate on frontend tests against new controller methods without having to rebuild the entire container image each time.

### Certbot renewal
`sudo podman exec -it aghanims-wager-spa certbot renew`
This should probably be set up in the crontab somehow but certbot tries to put a file into an nginx folder that it then hits which makes it tricky with a container.