
import asyncio, json
import datetime as dt

import discord
import discord.utils
from discord.ext import commands


import pandas as pd

import sqlalchemy as db

from tokens import TOKEN, CONNECTION_STRING

from pgdb import PGDB

pgdb = PGDB(CONNECTION_STRING)

class Lobby:

	msg_template = '''Live game: {match_id}
Last Update Time: {last_update_time}
Average MMR: {average_mmr}
Radiant Lead: {radiant_lead}
Radiant Score: {radiant_score}
Dire Score: {dire_score}
Building State: {building_state}'''
	
	def __init__(self,lobby_id,channel):
		self.lobby_id = lobby_id
		self.channel = channel
		self.last_update = None
		self.match_details = None
		self.message_id = None

		#self.update_announce(self)
				
	async def announce(self):
		live_df = pd.read_sql_query(pgdb.select_lm(self.lobby_id,query_only=True),con=pgdb.conn)
		last_update_df = live_df[live_df['query_time'] == live_df['query_time'].max()]
		self.match_details = list(last_update_df.to_dict().items())
		self.match_details = dict(map(lambda x: (x[0],next(iter(x[1].values()))),self.match_details))
		self.last_update = self.match_details['last_update_time']
		m = Lobby.msg_template.format(**self.match_details)
		if self.message_id:
			await self.match_message.edit(content=m)
		else:
			if (message_id := pgdb.select_message(self.lobby_id)):
				self.message_id = message_id[0]
				self.match_message = await self.channel.fetch_message(self.message_id)
			if self.message_id:
				print('using old message_id {}'.format(self.message_id))
				await self.match_message.edit(content=m)
			else:
				self.match_message = await self.channel.send(m)
				self.message_id = self.match_message.id
				print('making new old message_id {}'.format(self.message_id))
				pgdb.insert_message(self.lobby_id,self.message_id)
			
class DotaBet(commands.Bot):
	def __init__(self, *args, **kwargs):
		super().__init__(*args,**kwargs)
		with open('heroes.json') as f:
			hero_data = json.load(f)
			self.hero_data = dict(map(lambda x: (x['name'],(x['id'],x['localized_name'])),hero_data['heroes']))
	
		self.live_lobbies = {}
		self.bg_task = self.loop.create_task(self.check_games())

	async def check_games(self):
		await self.wait_until_ready()
		print('Logged in as',self.user.name,self.user.id)

		await self.do_stuff()

	async def do_stuff(self):
		channel = discord.utils.get(self.get_all_channels(), name='dota-bet')
		while True:
			await asyncio.sleep(10)

			for live_lobby_id in map(lambda x: x[0],pgdb.get_live()):
				
				if live_lobby_id in self.live_lobbies.keys():
					live_lobby = self.live_lobbies[live_lobby_id]
				else:
					live_lobby = Lobby(live_lobby_id,channel)
					self.live_lobbies[live_lobby_id] = live_lobby

				await live_lobby.announce()

				
bot = DotaBet(command_prefix='!', description='Kali Roulette')
bot.run(TOKEN)
