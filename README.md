# Aghanim's Wager

Bet on the outcomes of your friend's Dota 2 matches, win nutritious salt.
This project is two parts:
1. Using the python-steam and python-dota packages, write a steam/dota bot that listens for information from live dota games of the bot's steam friends. Dump it into a database in real time.
2. A discord bot written in python that pulls live game data from the database and presents the matches to a channel. bot offers gambling opportunities.

If you're here you're probably interested in the first part, which is in the file `DotaBet_gc.py`. This can run independently of the Discord bot. You'll need a postgres database with a schema named `Kali` to hook it all up. 

With the setup the code requires and just how bad parts of the code base are, I don't really ever expect to have this project as something people can run their own instance of. I'm making it available so that someone might be able to take DotaBet_GC and adapt it to their own purpose. I hardcoded too much of the SQL and I'm too lazy to change it. If you REALLY want to try, you can set the database credentials and the credentials of your steam account in the file `tokens.py`. I'm willing to field questions so long as they're specific.


