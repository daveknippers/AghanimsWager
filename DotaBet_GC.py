import logging, datetime, time, json
from collections import defaultdict

from pathlib import Path
from enum import Enum, auto

logging.basicConfig(format='[%(asctime)s] %(levelname)s %(name)s: %(message)s', level=logging.WARN)

import gevent

import sqlalchemy as db
from sqlalchemy import exc

from steam.client import SteamClient
from steam.enums import EPersonaState, EResult
from steam.enums.emsg import EMsg
from steam.utils.proto import proto_to_dict
from dota2.client import Dota2Client
from dota2.proto_enums import EDOTAGCMsg

from tokens import STEAM_BOT_ACCOUNT, STEAM_BOT_PASSWORD, CONNECTION_STRING

from app_db import PGDB, LP_STATUS

LAST_REFRESH_TIME = int(time.mktime(datetime.datetime.now().timetuple()))

client = SteamClient()
dota = Dota2Client(client)

SLEEP_TIME = 15

source_tv_lobbies = set()
live_players_processed = defaultdict(lambda: LP_STATUS.NOT_FOUND)
rp_heroes_processed = set()
FRIENDS_IN_DB = False

def add_rp_hero(lobby_id,steam_id,rich_presence):
	if (lobby_id,steam_id) not in rp_heroes_processed:
		try:
			param0 = rich_presence['param0']
			param1 = rich_presence['param1']
			param2 = rich_presence['param2']

		except KeyError:
			logging.warning('could not parse rich presence for param[0,1,2] in lobby_id {} steam_id {}'.format(lobby_id,steam_id))
		else:
			row = {}
			row['lobby_id'] = lobby_id
			row['steam_id'] = steam_id
			row['param0'] = param0
			row['param1'] = param1
			row['param2'] = param2
			rp_heroes_processed.add((lobby_id,steam_id))
			if not pgdb.insert_rp(row):
				msg = 'rp status already in database for lobby_id {} steam_id {}.'.format(lobby_id,steam_id)
				msg = msg+' this is normal if restarting bot in middle of live games.'
				logging.warning(msg)

@client.on(EMsg.ClientPersonaState)
def investigate_ClientPersonaState(msg):
	if client.friends.ready:
		logging.warning('clearing source_tv_lobbies')
		source_tv_lobbies.clear()
		for f in client.friends:
			if f.state != EPersonaState.Offline:
				try:
					status = f.rich_presence['status']
					try:
						if f.rich_presence['param0'] == '#DOTA_lobby_type_name_lobby':
							continue
						if f.rich_presence['param0'] == '#game_mode_18':
							continue
						if f.rich_presence['param0'] == '#game_mode_23':
							continue
					except KeyError:
						continue
					if status == '#DOTA_RP_PLAYING_AS':
						watchable_game_id = int(f.rich_presence['WatchableGameID'])
						steam_id = f.steam_id
						if watchable_game_id != 0:
							source_tv_lobbies.add(watchable_game_id)
							add_rp_hero(watchable_game_id,steam_id,f.rich_presence)
					elif status == '#DOTA_RP_HERO_SELECTION' or status == '#DOTA_RP_STRATEGY_TIME':
						watchable_game_id = int(f.rich_presence['WatchableGameID'])
						if watchable_game_id != 0:
							source_tv_lobbies.add(watchable_game_id)
				except KeyError:
					pass
	
@dota.on(EDOTAGCMsg.EMsgGCToClientFindTopSourceTVGamesResponse)
def process_lobby_states(msg):
	logging.warning('Starting process_lobby_states')
	query_time = int(time.mktime(datetime.datetime.now().timetuple()))
	if msg.specific_games:	
		logging.warning('Specific lobbies returned')
		for g in msg.game_list:
			game = proto_to_dict(g)
			logging.warning('match_id: {}'.format(game['match_id']))
			logging.warning('game_time: {}'.format(game['game_time']))
			match_id = game['match_id']
			game['query_time'] = query_time
			players = game['players']
			del game['players']
			pgdb.insert_lm(game)
			if live_players_processed[match_id] == LP_STATUS.NOT_FOUND:
				live_players_processed[match_id] = pgdb.check_lp(match_id)
			if live_players_processed[match_id] == LP_STATUS.NOT_FOUND:
				for p,i in zip(players,range(len(players))):
					p['match_id'] = match_id
					p['player_num'] = i
				live_players_processed[match_id] = pgdb.insert_lp(players)
			elif live_players_processed[match_id] == LP_STATUS.INIT:
				if not any(map(lambda x: x == 0,map(lambda x: x['hero_id'],players))):
					for p,i in zip(players,range(len(players))):
						p['match_id'] = match_id
						p['player_num'] = i
					live_players_processed[match_id] = pgdb.update_lp(players)
	else:
		logging.info('No specific lobbies returned')

@client.on('logged_on')
def start_dota():
	logging.warning('Starting Dota GC communication')
	dota.launch()

@dota.on(EDOTAGCMsg.EMsgGCMatchDetailsResponse)
def process_match_object(msg):
	game = proto_to_dict(msg)
	match_id = game['match']['match_id']
	with open(extended_match_details_path / '{}_extended.json'.format(match_id), 'w') as f_out:
		json.dump(game, f_out)
		pgdb.update_extended_match_details_requests(match_id)

EXTENDED_MATCH_DETAILS_REQUESTED = set()
CHECKING_NOW = False

def check_for_replays():
	global CHECKING_NOW
	if CHECKING_NOW:
		return
	else:
		CHECKING_NOW = True
	results = pgdb.get_extended_match_details_requests()
	for match_id in results:
		match_id = match_id[0]
		logging.warning('checking for extended details on {}'.format(match_id))
		if not ((extended_match_details_path / '{}_extended.json'.format(match_id)).exists()):
			if match_id not in EXTENDED_MATCH_DETAILS_REQUESTED:
				dota.request_match_details(match_id)
				EXTENDED_MATCH_DETAILS_REQUESTED.add(match_id)

				# this could get messy if there's a lot of extended match detail requests in queue...
				gevent.sleep(1)
		else:
			msg = str(extended_match_details_path / '{}_extended.json'.format(match_id)) + ' already exists'
			logging.warning(msg)
	CHECKING_NOW = False
	

@dota.on('ready')
def lobby_loop():
	global FRIENDS_IN_DB, LAST_REFRESH_TIME
	logging.info('Dota GC communications prepared')
	current_live_lobbies = set()
	pgdb.replace_live(current_live_lobbies)
	LAST_REFRESH_TIME = int(time.mktime(datetime.datetime.now().timetuple()))
	start_time = LAST_REFRESH_TIME
	while start_time == LAST_REFRESH_TIME:
		if not FRIENDS_IN_DB and client.friends.ready:
			friend_ids = list(map(lambda x: int(x.steam_id),client.friends))
			pgdb.replace_friends(friend_ids)
			FRIENDS_IN_DB = True
			

		gevent.sleep(SLEEP_TIME)
		logging.warning('Checking lobbies...')
		logging.warning('current_live_lobbies: {}'.format(current_live_lobbies))
		logging.warning('source_tv_lobbies: {}'.format(source_tv_lobbies))
		n_lobbies = len(source_tv_lobbies)
		if n_lobbies > 0:
			if current_live_lobbies != source_tv_lobbies:
				pgdb.replace_live(source_tv_lobbies)
				current_live_lobbies = source_tv_lobbies.copy()
			data = dict([('lobby_ids',list(source_tv_lobbies))])
			logging.info('Checking {} lobbies'.format(n_lobbies))
			dota.send(EDOTAGCMsg.EMsgClientToGCFindTopSourceTVGames,data)
		elif n_lobbies == 0 and len(current_live_lobbies) != 0:
			current_live_lobbies = set()
			pgdb.replace_live(source_tv_lobbies)
		check_for_replays()

@client.on('disconnect')
def handle_disconnect():
	logging.warning('Steam disconnected')
	if client.relogin_available:
		client.reconnect()

pgdb = PGDB(CONNECTION_STRING,'DotaBet_GC')
	
result = client.cli_login(username=STEAM_BOT_ACCOUNT,password=STEAM_BOT_PASSWORD)

extended_match_details_path = Path.cwd() / 'extended_match_details'
extended_match_details_path.mkdir(exist_ok=True)

if result != EResult.OK:
	logging.error('Could not log in')	
	raise SystemExit
client.run_forever()

