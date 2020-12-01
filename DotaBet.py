
import asyncio, json, datetime, re

import discord
import discord.utils
from discord.ext import commands

import pandas as pd

import sqlalchemy as db

from tokens import TOKEN, CONNECTION_STRING

from app_db import PGDB

pgdb = PGDB(CONNECTION_STRING)
with open('heroes.json') as f:
	hero_data = json.load(f)
	hero_data = dict(map(lambda x: (x['id'],(x['name'],x['localized_name'])),hero_data['heroes']))
	hero_data[0] = ('null','null')

def convert_steam_to_account(steam_id):
	id_str = str(steam_id)
	id64_base = 76561197960265728
	offset_id = int(id_str) - id64_base
	account_type = offset_id % 2
	account_id = ((offset_id - account_type) // 2) + account_type
	return int(str((account_id * 2) - account_type))

class Lobby:

	msg_template = '''Live game: {match_id}
Game Time: {game_time}
Average MMR: {average_mmr}
Radiant Lead: {radiant_lead}
Radiant Score: {radiant_score}
Dire Score: {dire_score}
Building State: {building_state}'''
	
	def __init__(self,lobby_id,channel,friend_df):
		self.lobby_id = lobby_id
		self.channel = channel
		self.friend_df = friend_df
		self.message_id = None
		self.last_update = None
		self.match_details = None
		self.old_message = None
		self.player_id = None
		self.match_id = None

		self.player_msg_obj = None
		self.player_msg = None

	async def announce(self):
		db_result,columns = pgdb.select_lm(self.lobby_id,self.last_update)
		live_df = pd.DataFrame(db_result,columns=columns)
		if len(live_df) == 0:
			# should instead check if we're about to write the exact same thing, last_update isn't as robust as i assumed
			print('no new live games found since {}'.format(self.last_update))
			return
		start_last_update = self.last_update
		last_update_df = live_df[live_df['query_time'] == live_df['query_time'].max()]
		self.match_details = list(last_update_df.to_dict().items())
		self.match_details = dict(map(lambda x: (x[0],next(iter(x[1].values()))),self.match_details))
	
		self.match_id = self.match_details['match_id']
		player_table,columns = pgdb.read_lp(self.match_id)
		player_df = pd.DataFrame(player_table,columns=columns)
		player_df = pd.merge(player_df,self.friend_df,how='left',on=['account_id'])
		player_df['hero_name'] = player_df['hero_id'].apply(lambda x: hero_data[x][1])
		player_msg_template = '{player_num} {hero_name} {account_id} {steam_id}'.format
		player_msg = player_df.apply(lambda x: player_msg_template(**x),1)
		player_msg = '\n'.join(player_msg.values)

		if self.player_msg_obj == None:
			self.player_msg_obj = await self.channel.send(player_msg)
			self.player_msg = player_msg
		elif player_msg != self.player_msg:
			await self.player_msg_obj.edit(content=player_msg)
			self.player_msg = player_msg
			
		self.last_update = self.match_details['last_update_time']
		print('updating {} to {}'.format(start_last_update,self.last_update))
		m = Lobby.msg_template.format(**self.match_details)
		if self.old_message != None and m == self.old_message:
			print('leaving without editing')
			return
		else:
			if self.message_id:
				print('editing message {}'.format(self.message_id))
				await self.match_message.edit(content=m)
				self.old_message = m
			else:
				if (message_id := pgdb.select_message(self.lobby_id)):
					self.message_id = message_id[0][0]
					self.match_message = await self.channel.fetch_message(self.message_id)
				if self.message_id:
					print('using old message_id {}'.format(self.message_id))
					await self.match_message.edit(content=m)
				else:
					self.match_message = await self.channel.send(m)
					self.message_id = self.match_message.id
					print('making new message_id {}'.format(self.message_id))
					pgdb.insert_message(self.lobby_id,self.message_id)
				self.old_message = m
			
class DotaBet(commands.Bot):
	def __init__(self, *args, **kwargs):
		super().__init__(*args,**kwargs)
	
		self.live_lobbies = {}
		self.bg_task = self.loop.create_task(self.check_games())

	async def check_games(self):
		await self.wait_until_ready()
		print('Logged in as',self.user.name,self.user.id)

		channel = discord.utils.get(self.get_all_channels(), name='dota-bet')

		friend_df = pd.DataFrame(map(lambda x: x[0],pgdb.select_friends()),columns=['steam_id'],dtype=pd.Int64Dtype())
		friend_df['account_id'] = friend_df['steam_id'].apply(convert_steam_to_account)

		while True:
			await asyncio.sleep(5)

			live_lobbies = pgdb.get_live()
			live_lobbies = list(map(lambda x: x[0],live_lobbies))

			for live_lobby_id in live_lobbies:
				print('lobby found: {}'.format(live_lobby_id))
				
				if live_lobby_id in self.live_lobbies.keys():
					live_lobby = self.live_lobbies[live_lobby_id]
				else:
					live_lobby = Lobby(live_lobby_id,channel,friend_df)
					self.live_lobbies[live_lobby_id] = live_lobby

				await live_lobby.announce()
			print('finished iterating through {} lobbies'.format(len(live_lobbies)))


bot = DotaBet(command_prefix='!', description='Kali Roulette')
bot.run(TOKEN)
