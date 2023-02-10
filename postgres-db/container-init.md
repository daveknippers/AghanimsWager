### Ran these commands to get started
podman pull docker.io/library/postgres:bullseye
podman run -d --name postgres -p 5432:5432 -e POSTGRES_PASSWORD=<password> -v postgres:/var/lib/postgresql/data postgres:bullseye