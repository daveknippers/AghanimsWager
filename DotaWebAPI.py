import json, os, time, datetime

import requests

from app_db import MATCH_STATUS
from tokens import STEAM_API_KEY

url_GetMatchDetails = 'https://api.steampowered.com/IDOTA2Match_570/GetMatchDetails/v001/'

def schuck_match_details(match_id):
	json_match_details = request_match_details(match_id)
	if not json_match_details:
		return MATCH_STATUS.UNRESOLVED
	result = json_match_details['result']
	if 'error' in result:
		print('Fetching {} failed: {}'.format(match_id,result['error']))
		return MATCH_STATUS.UNRESOLVED
	elif 'radiant_win' in result:
		json_file = 'match_details' + os.sep + str(match_id) + '.json'
		try:
			with open(json_file, 'w') as f:
				json.dump(json_match_details,f)
			print('dumped file at {}'.format(json_file))
		except IOError:
			print('Cannot write {}'.format(json_file))
		if result['radiant_win']:
			return MATCH_STATUS.RADIANT
		else:
			return MATCH_STATUS.DIRE
	else:
		json_file = 'match_details' + os.sep + str(match_id) + '.json'
		try:
			with open(json_file, 'w') as f:
				json.dump(json_match_details,f)
		except IOError:
			print('Cannot write {}'.format(json_file))
		print('Cannot parse match json data written at {}'.format(json_file))
		return MATCH_STATUS.ERROR
		

def request_match_details(match_id):
	req = {}
	req['key'] = STEAM_API_KEY
	req['match_id'] = match_id

	response = requests.get(url_GetMatchDetails,params = req,timeout = 5)
	status = response.status_code

	if status == 200:
		return response.json()
	else:
		print('request_match_details {} get return status: {}'.format(match_id,status))
		return None

if __name__ == '__main__':
	schuck_match_details(5732005218)

