
import asyncio, json, datetime, re

from asyncio_dispatch import Signal

import discord
import discord.utils
from discord.ext import commands

import pandas as pd

import sqlalchemy as db

from tokens import TOKEN, CONNECTION_STRING

from app_db import PGDB, MATCH_STATUS

from DotaWebAPI import schuck_match_details

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

		self.winner = MATCH_STATUS.UNRESOLVED

	async def init_msg_objs(self):
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

	async def update(self, match_details, player_msg):
		match_msg = MatchMsg.match_msg_template.format(**match_details)

		if self.winner == MATCH_STATUS.UNRESOLVED:
			winning_side = 'Match In Progress'
		elif self.winner == MATCH_STATUS.RADIANT:
			winning_side = 'Radiant'
		elif self.winner == MATCH_STATUS.DIRE:
			winning_side = 'Dire'
		elif self.winner == MATCH_STATUS.ERROR:
			winning_side = 'Error parsing match result'
		match_msg += '\nMatch Status: {}'.format(winning_side)

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

	async def announce_winner(self,winner):
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
			self.match_msg = self.match_msg.replace('Match In Progress',w)
			await self.match_msg_obj.edit(content=self.match_msg)
			print('edited match_msg {}'.format(self.match_msg_id))
		else:
			print('cannot announce winner with non-existent lobby_id {}'.format(self.lobby_id))
		
class Lobby:
	player_msg_template = '{player_num} {hero_name} {account_id} {steam_id}'.format

	def __init__(self,lobby_id,channel,friend_df):
		self.lobby_id = lobby_id
		self.friend_df = friend_df
		self.match_id = None
		self.last_update = None

		self.match_obj = MatchMsg(channel,lobby_id)

	async def check_match_details(self):
		print('checking match details for {}'.format(self.match_id))
		status = schuck_match_details(self.match_id)
		if status == MATCH_STATUS.UNRESOLVED:
			print('match id {} has unresolved match details'.format(self.match_id))
			return
		else:
			print('match id {} winner is {}'.format(self.match_id,status))
			await self.match_obj.announce_winner(status)
			pgdb.update_match_status(self.match_id,status)

	async def announce(self):
		db_result,columns = pgdb.select_lm(self.lobby_id,self.last_update)
		live_df = pd.DataFrame(db_result,columns=columns)
		last_update_df = live_df[live_df['query_time'] == live_df['query_time'].max()]
		match_details = list(last_update_df.to_dict().items())
		match_details = dict(map(lambda x: (x[0],next(iter(x[1].values()))),match_details))

		self.last_update = match_details['last_update_time']

		if self.match_id == None:
			self.match_id = match_details['match_id']
			check_match_status = pgdb.check_match_status(self.match_id)
			if len(check_match_status) == 0:
				pgdb.insert_match_status(self.match_id)
			else:
				check_match_status = check_match_status[0][0]
				if check_match_status != MATCH_STATUS.UNRESOLVED:
					print('match {} already complete, result: {}'.format(self.match_id,check_match_status))
					await self.match_obj.announce_winner(check_match_status)
				else:
					print('match {} in progress'.format(self.match_id))
					
		player_table,columns = pgdb.read_lp(self.match_id)

		player_df = pd.DataFrame(player_table,columns=columns)
		player_df = pd.merge(player_df,self.friend_df,how='left',on=['account_id'])
		player_df['hero_name'] = player_df['hero_id'].apply(lambda x: hero_data[x][1])

		player_msg = player_df.apply(lambda x: Lobby.player_msg_template(**x),1)
		player_msg = '\n'.join(player_msg.values)

		await self.match_obj.update(match_details,player_msg)

class BookKeeper(commands.Bot):
	def __init__(self, *args, **kwargs):
		super().__init__(*args,**kwargs)

		self.lobby_objs = {}
		self.waiting_on = set()

		self.channel = None
		self.friend_df = None

		self.lobby_listener = self.loop.create_task(self.check_lobbies_loop())

	async def check_lobbies_loop(self):
		await self.wait_until_ready()
		print('Logged in as',self.user.name,self.user.id)

		self.channel = discord.utils.get(self.get_all_channels(), name='dota-bet')

		self.friend_df = pd.DataFrame(map(lambda x: x[0],pgdb.select_friends()),columns=['steam_id'],dtype=pd.Int64Dtype())
		self.friend_df['account_id'] = self.friend_df['steam_id'].apply(convert_steam_to_account)

		active_lobbies = set(map(lambda x: x[0],pgdb.get_unresolved_matches()))
		for lobby_id in active_lobbies:
			print('retrieving lobby id {}'.format(lobby_id))
			await self.refresh_lobby(lobby_id)
			
		while True:
			print('****************************')
			await asyncio.sleep(5)

			live_lobbies = pgdb.get_live()
			live_lobbies = set(map(lambda x: x[0],live_lobbies))

			for live_lobby_id in live_lobbies:
				await self.refresh_lobby(live_lobby_id)

			zombie_lobbies = active_lobbies - live_lobbies

			for zombie in zombie_lobbies:
				print('checking for finished match for lobby id {}'.format(zombie))
				await self.lobby_objs[zombie].check_match_details()

			active_lobbies = live_lobbies

			print('finished iterating through {} lobbies'.format(len(live_lobbies)))

	async def refresh_lobby(self,live_lobby_id):
		print('lobby found: {}'.format(live_lobby_id))

		if live_lobby_id in self.lobby_objs.keys():
			live_lobby = self.lobby_objs[live_lobby_id]
		else:
			live_lobby = Lobby(live_lobby_id,self.channel,self.friend_df)
			await live_lobby.match_obj.init_msg_objs()
			self.lobby_objs[live_lobby_id] = live_lobby

		await live_lobby.announce()


bot = BookKeeper(command_prefix='!', description='DotaBet')

@bot.command()
async def add_steam_id(ctx,*arg):
	if len(arg) != 1:
		await ctx.send('Invalid syntax, try !add_steam_id steam_id')
		return
	steam_id = arg[0]
	discord_id = ctx.message.author.id
	if len(steam_id) != 17 or not str(steam_id).isdigit():
		await ctx.send('Invalid syntax, steam_id is a 17 digit number')
	else:
		if (result := pgdb.insert_discord_id(discord_id,steam_id)):
			await ctx.message.channel.send('Added steam_id {} for {}'.format(steam_id,ctx.message.author.mention))
			


bot.run(TOKEN)

