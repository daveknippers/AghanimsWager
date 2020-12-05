
import asyncio, json, datetime, re

from asyncio_dispatch import Signal

import discord
import discord.utils
from discord.ext import commands

import pandas as pd

import sqlalchemy as db

from tokens import TOKEN, CONNECTION_STRING

from app_db import PGDB, MATCH_STATUS

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

class MatchMsg:

	match_msg_template = '''Live game: {match_id}
Game Time: {game_time}
Average MMR: {average_mmr}
Radiant Lead: {radiant_lead}
Radiant Score: {radiant_score}
Dire Score: {dire_score}
Building State: {building_state}'''


	def __init__(self,channel,lobby_id):
		self.channel = channel
		self.lobby_id = lobby_id

		self.match_msg = None
		self.match_msg_obj = None
		self.match_msg_id = None

		self.player_msg = None
		self.player_msg_obj = None
		self.player_msg_id = None

		self.last_update = None
		self.winner = MATCH_STATUS.UNRESOLVED

		self.init_msg_objs()

	def init_msg_objs(self):
		if self.player_msg_obj == None:
			if (player_msg_id := pgdb.select_players_message(self.lobby_id)):
				print('using player_msg_id {} for lobby {}'.format(self.player_msg_id,self.lobby_id))
				self.player_msg_id = player_msg_id[0][0]
				self.player_msg_obj = await self.channel.fetch_message(self.player_msg_id)
				self.player_msg = self.player_msg_obj.content

		if self.match_msg_obj == None:	
			if (match_msg_id := pgdb.select_match_message(self.lobby_id)):
				print('using match_msg_id {} for lobby {}'.format(self.match_msg_id,self.lobby_id))
				self.match_msg_id = match_msg_id[0][0]
				self.match_msg_obj = await self.channel.fetch_message(self.match_msg_id)
				self.match_msg = self.match_msg_obj.content

	def update(self, match_details, player_msg):
		match_msg = Lobby.match_summary_template.format(**match_details)

		if self.winner == MATCH_STATUS.UNRESOLVED:
			winning_side = 'In Progress'
		elif self.winner == MATCH_STATUS.RADIANT:
			winning_side = 'Radiant'
		elif self.winner == MATCH_STATUS.DIRE:
			winning_side = 'Dire'
		match_msg += '\nWinner: {}'.format(winning_side)

		if self.player_msg_obj == None:
			self.player_msg_obj = await self.channel.send(player_msg)
			self.player_msg = player_msg
			self.player_msg_id = self.player_msg_obj.id
			print('new player_msg_id {} for lobby {}'.format(self.player_msg_id,self.lobby_id))
			pgdb.insert_players_message(self.lobby_id,self.player_msg_id)
		else:
			if self.player_msg != player_msg:
				await self.player_msg_obj.edit(content=player_msg)
				print('edited player_msg {}'.format(self.player_msg_id))
				self.player_msg = player_msg
			else:
				print('no update for player_msg_id {}'.format(self.player_msg_id,self.lobby_id))

		if self.match_msg_obj == None:
			self.match_msg_obj = await self.channel.send(match_msg)
			self.match_msg = match_msg
			self.match_msg_id = self.match_msg_obj.id
			print('new match_msg_id {} for lobby {}'.format(self.match_msg_id,self.lobby_id))
			pgdb.insert_match_message(self.lobby_id,self.match_msg_id)
		else:
			if self.match_msg != match_msg:
				await self.match_msg_obj.edit(content=match_msg)
				print('edited match_msg {}'.format(self.match_msg_id))
				self.match_msg = match_msg
			else:
				print('no update for match_msg_id {}'.format(self.match_msg_id,self.lobby_id))

	def announce_winner(winner):
		if winner == MATCH_STATUS.RADIANT:
			w = 'Radiant'
		elif winner == MATCH_STATUS.DIRE:
			w = 'Dire'
		else:
			print('announce winner called without winner for lobby id {}'.format(self.lobby_id))
			return
		self.winner = winner
		if self.match_msg_obj != None:
			self.match_msg = self.match_msg_obj.content
			self.match_msg = self.match_msg.replace('In Progress',w)
			await self.match_msg_obj.edit(content=match_msg)
			print('edited match_msg {}'.format(self.match_msg_id))
		else:
			print('cannot announce winner with non-existent lobby_id {}'.format(self.lobby_id))
		
class Lobby:
	player_msg_template = '{player_num} {hero_name} {account_id} {steam_id}'.format

	def __init__(self,lobby_id,channel,friend_df):
		self.lobby_id = lobby_id
		self.friend_df = friend_df
		self.match_id = None
		self.check_match_status = None

		self.match_obj = MatchMsg(lobby_id,channel)

	async def announce(self):
		db_result,columns = pgdb.select_lm(self.lobby_id,self.last_update)
		live_df = pd.DataFrame(db_result,columns=columns)
		last_update_df = live_df[live_df['query_time'] == live_df['query_time'].max()]
		match_details = list(last_update_df.to_dict().items())
		match_details = dict(map(lambda x: (x[0],next(iter(x[1].values()))),match_details))

		if self.match_id == None:
			self.match_id = match_details['match_id']
			self.check_match_status = pgdb.check_match_status(self.match_id)
			if self.check_match_status == None:
				pgdb.insert_match_status(self.match_id)
			elif self.check_match_status != MATCH_STATUS.UNRESOLVED:
				self.match_obj.announce_winner(self.check_match_status)
				
		player_table,columns = pgdb.read_lp(self.match_id)

		player_df = pd.DataFrame(player_table,columns=columns)
		player_df = pd.merge(player_df,self.friend_df,how='left',on=['account_id'])
		player_df['hero_name'] = player_df['hero_id'].apply(lambda x: hero_data[x][1])

		player_msg = player_df.apply(lambda x: Lobby.player_msg_template(**x),1)
		player_msg = '\n'.join(player_msg.values)

		self.match_obj.update(match_details,player_msg)

class BookKeeper(commands.Bot):
	def __init__(self, *args, **kwargs):
		super().__init__(*args,**kwargs)

		self.open_matches = set()
		self.live_lobbies = set()

		self.lobby_listener = self.loop.create_task(self.check_lobbies_loop())
		self.match_results_listener = self.loop.create_task(Lobby.check_match_results_loop())

	async def check_match_results_loop(self):
		unresolved_matches = pgdb.get_unresolved_matches()

	async def check_lobbies_loop(self):
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
				await self.refresh_lobby(live_lobby_id)

			print('finished iterating through {} lobbies'.format(len(live_lobbies)))

	async def refresh_lobby(self,live_lobby_id):
		print('lobby found: {}'.format(live_lobby_id))
		
		if live_lobby_id in self.live_lobbies.keys():
			live_lobby = self.live_lobbies[live_lobby_id]
		else:
			live_lobby = Lobby(live_lobby_id,channel,friend_df)
			self.live_lobbies[live_lobby_id] = live_lobby

		await live_lobby.announce()


bot = BookKeeper(command_prefix='!', description='DotaBet')
bot.run(TOKEN)
