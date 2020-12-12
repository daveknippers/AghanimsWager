
import asyncio, json, datetime, re, time

from asyncio_dispatch import Signal

import discord
import discord.utils
from discord.ext import commands

import pandas as pd

import sqlalchemy as db

from tokens import TOKEN, CONNECTION_STRING

from app_db import PGDB, MATCH_STATUS

from DotaWebAPI import schuck_match_details

GAMBLING_CLOSE_WAIT = 45

CURRENCY = 'golden salt'
INFO_CHANNEL = 'dota-bet'
COMM_CHANNEL = 'dota2'

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

	players_msg_template = '''```=========== Radiant ==========
------------------------------
--\t{0}
--\t{1}
--\t{2}
--\t{3}
--\t{4}
------------------------------
=========== Dire =============
------------------------------
--\t{5}
--\t{6}
--\t{7}
--\t{8}
--\t{9}
------------------------------\n'''

	match_msg_template = '''Match ID: {match_id}
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

		self.winner = MATCH_STATUS.UNRESOLVED
		self.gambling_close = None

	async def init_msg_objs(self):
		if self.match_msg_obj == None:	
			if (match_msg_id := pgdb.select_match_message(self.lobby_id)):
				print('using match_msg_id {} for lobby {}'.format(self.match_msg_id,self.lobby_id))
				self.match_msg_id = match_msg_id[0][0]
				self.match_msg_obj = await self.channel.fetch_message(self.match_msg_id)
				self.match_msg = self.match_msg_obj.content

	async def update(self, match_details, players_msgs, pick_phase):
		match_msg = MatchMsg.match_msg_template.format(**match_details)
		players_msg = MatchMsg.players_msg_template.format(*players_msgs)
		
		match_msg = players_msg+match_msg

		if self.winner == MATCH_STATUS.UNRESOLVED:
			winning_side = 'Match In Progress'
		elif self.winner == MATCH_STATUS.RADIANT:
			winning_side = 'Radiant'
		elif self.winner == MATCH_STATUS.DIRE:
			winning_side = 'Dire'
		elif self.winner == MATCH_STATUS.ERROR:
			winning_side = 'Error parsing match result'
		match_msg += '\nMatch Winner: {}'.format(winning_side)

		if pick_phase:
			self.gambling_message = 'Bets are open'

		elif not pick_phase and not self.gambling_close:
			if match_details['game_time'] > 200:
				self.gambling_close = int(time.mktime(datetime.datetime.now().timetuple()))
				self.gambling_message = 'Bets are closed'
			else:
				self.gambling_close = int(time.mktime(datetime.datetime.now().timetuple()))+GAMBLING_CLOSE_WAIT
				self.gambling_message = 'Bets are closing in ~45 second(s)'
		elif not pick_phase:
			current_time = int(time.mktime(datetime.datetime.now().timetuple()))
			if current_time > self.gambling_close:
				self.gambling_message = 'Bets are closed'
			else:
				remaining = self.gambling_close - current_time
				self.gambling_message = 'Bets are closing in ~{} second(s)'.format(remaining)

		match_msg += '\n\n'+self.gambling_message+'```'

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
		elif winner == MATCH_STATUS.RADIANT:
			w = 'Not scored'
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
	player_msg_template = '{hero_name} {discord_name}'.format

	def __init__(self,lobby_id,channel,friend_df,client):
		self.lobby_id = lobby_id
		self.friend_df = friend_df
		self.client = client
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
		if len(db_result) == 0:
			return
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
		pick_phase = any(player_df['hero_id'] == 0)
		player_df.sort_values(by=['player_num'],inplace=True)
		player_df['hero_name'] = player_df['hero_id'].apply(lambda x: hero_data[x][1])

		player_df['discord_name'] = player_df['discord_name'].fillna('')

		player_msg = player_df.apply(lambda x: Lobby.player_msg_template(**x),1)

		await self.match_obj.update(match_details,player_msg.values,pick_phase)

class BookKeeper(commands.Bot):
	def __init__(self, *args, **kwargs):
		super().__init__(*args,**kwargs)

		self.lobby_objs = {}
		self.waiting_on = set()

		self.channel = None
		self.friend_df = None

		self.discord_mapping = {}

		self.lobby_listener = self.loop.create_task(self.check_lobbies_loop())

	async def check_lobbies_loop(self):
		await self.wait_until_ready()
		print('Logged in as',self.user.name,self.user.id)

		self.channel = discord.utils.get(self.get_all_channels(), name=INFO_CHANNEL)

		discord_id_df = pd.DataFrame(pgdb.select_discord_ids(),columns=['discord_id','steam_id'],dtype=pd.Int64Dtype())

		self.friend_df = pd.DataFrame(map(lambda x: x[0],pgdb.select_friends()),columns=['steam_id'],dtype=pd.Int64Dtype())
		self.friend_df['account_id'] = self.friend_df['steam_id'].apply(convert_steam_to_account)
		self.friend_df = pd.merge(discord_id_df,self.friend_df,how='left')

		for index,row in self.friend_df.iterrows():
			self.discord_mapping[row['discord_id']] = await self.fetch_user(row['discord_id'])

		self.friend_df['discord_name'] = self.friend_df['discord_id'].apply(lambda x: '| ' + self.discord_mapping[x].name)
		print(self.friend_df['discord_name'])

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
			live_lobby = Lobby(live_lobby_id,self.channel,self.friend_df,self)
			await live_lobby.match_obj.init_msg_objs()
			self.lobby_objs[live_lobby_id] = live_lobby

		await live_lobby.announce()


bot = BookKeeper(command_prefix='!', description='DotaBet')

@bot.command()
async def add_steam_id(ctx,*arg):
	if ctx.guild is not None and str(ctx.message.channel) != COMM_CHANNEL:
		return
	if len(arg) != 1:
		await ctx.send('Invalid syntax, try !add_steam_id steam_id')
		return
	steam_id = arg[0]
	discord_id = ctx.message.author.id
	if len(steam_id) != 17 or not str(steam_id).isdigit():
		await ctx.send('Invalid syntax, steam_id is a 17 digit number')
	else:
		if (result := pgdb.insert_discord_id(discord_id,steam_id)):
			await ctx.send('Added steam_id {} for {}'.format(steam_id,ctx.message.author.mention))

@bot.command()
async def hi(ctx,*arg):
	if ctx.guild is None or str(ctx.message.channel) == COMM_CHANNEL:
		await ctx.send('{} ?'.format(ctx.message.author.mention))

@bot.command()
async def balance(ctx,*arg):
	if ctx.guild is not None and str(ctx.message.channel) != COMM_CHANNEL:
		return
	amount = pgdb.check_balance(ctx.message.author.id)
	if amount != 1:
		msg = '{0.author.mention}, you have {1} {2}s'.format(ctx.message,amount,CURRENCY)
	else:
		msg = '{0.author.mention}, you have {1} {2}'.format(ctx.message,amount,CURRENCY)
	await ctx.send(msg)

@bot.command()
async def bet(ctx,*arg):
	if ctx.guild is not None and str(ctx.message.channel) != COMM_CHANNEL:
		return
	if len(arg) != 3:
		await ctx.send('{}, try !bet match_id side amount'.format(ctx.message.author.mention))
		return 
	match_id,side,amount = arg
	if not match_id.isdigit():
		await ctx.send('{}, match_id must be an integer'.format(ctx.message.author.mention))
		return 
	if side.lower() not in ['radiant','dire']:
		await ctx.send('{}, must enter side as either radiant or dire'.format(ctx.message.author.mention))
		return
	if not amount.isdigit():
		await ctx.send('{}, amount must be an integer'.format(ctx.message.author.mention))
		return
	await ctx.send('{}, this is where your bet would be processed if i wrote the code'.format(ctx.message.author.mention))
		
	
		

bot.run(TOKEN)


