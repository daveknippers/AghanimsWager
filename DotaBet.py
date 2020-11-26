
import asyncio
import datetime as dt

import discord
import discord.utils
from discord.ext import commands


import pandas as pd

import sqlalchemy as db

from tokens import TOKEN, CONNECTION_STRING

from pgdb import PGDB

pgdb = PGDB(CONNECTION_STRING)

last_time = set()

class DotaBet(commands.Bot):
	def __init__(self, *args, **kwargs):
		super().__init__(*args,**kwargs)
	
		self.bg_task = self.loop.create_task(self.check_games())

	async def check_games(self):
		await self.wait_until_ready()
		print('Logged in as',self.user.name,self.user.id)

		await self.do_stuff()

	async def do_stuff(self):
		channel = discord.utils.get(self.get_all_channels(), name='dota-bet')
		while True:
			await asyncio.sleep(3)

			for live in map(lambda x: x[0],pgdb.get_live()):
				live_df = pd.read_sql_query(pgdb.select_lm(live,query_only=True),con=pgdb.conn)
				last_update_df = live_df[live_df['query_time'] == live_df['query_time'].max()]
				output_format = list(last_update_df.to_dict().items())
				output_format = dict(map(lambda x: (x[0],next(iter(x[1].values()))),output_format))
				if (live,output_format['last_update_time']) not in last_time:
					msg = '''Live game: {match_id}
Last Update Time: {last_update_time}
Average MMR: {average_mmr}
Radiant Lead: {radiant_lead}
Radiant Score: {radiant_score}
Dire Score: {dire_score}
Building State: {building_state}'''.format(**output_format)
					last_time.add((live,output_format['last_update_time']))
					await channel.send(msg)

bot = DotaBet(command_prefix='!', description='Kali Roulette')
bot.run(TOKEN)
